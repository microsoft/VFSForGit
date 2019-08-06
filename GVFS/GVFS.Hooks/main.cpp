#include "stdafx.h"
#include "common.h"
#include "Process.h"
#include "Upgrader.h"
#include "GVFSLock.h"
#include "Console.h"
#include "Messages.h"
#include "KnownGitCommands.h"
#include "GVFSEnvironment.h"
#include "String.h"

using std::find_if;
using std::function;
using std::ostringstream;
using std::string;
using std::to_string;
using std::transform;
using std::vector;

enum class HookType
{
    Invalid = 0,
    PreCommand = 1,
    PostCommand = 2,
};

static const string PreCommandHook = "pre-command";
static const string PostCommandHook = "post-command";
static const string GitPidArg = "--git-pid=";
static const int InvalidProcessId = -1;
static const int PostCommandSpinnerDelayMs = 500;
static const string ReminderNotification = "\nA new version of GVFS is available. Run `gvfs upgrade --confirm` from an elevated command prompt to upgrade.\n";

static PATH_STRING s_pipeName;

static inline void RunPreCommands(char *argv[]);
static inline void RunPostCommands(bool unattended);
static inline void RemindUpgradeAvailable();
static inline void ExitWithError(const std::string& error);
static inline void CheckForLegalCommands(char* argv[]);
static void RunLockRequest(int argc, char *argv[], bool unattended, function<void(bool, int, char*[], int, PIPE_HANDLE)> requestToRun);
static string GenerateFullCommand(int argc, char* argv[]);
static int GetParentPid(int argc, char* argv[]);
static void AcquireGVFSLockForProcess(bool unattended, int argc, char* argv[], int pid, PIPE_HANDLE pipeClient);
void ReleaseGVFSLock(bool unattended, int argc, char* argv[], int pid, PIPE_HANDLE pipeClient);
static bool CheckGVFSLockAvailabilityOnly(int argc, char *argv[]);
static string BuildUpdatePlaceholderFailureMessage(vector<string>& fileList, const string& failedOperation, const string& recoveryCommand);
static bool IsGitEnvVarDisabled(const string& envVar);
static bool ShouldLock(int argc, char* argv[]);
static inline HookType GetHookType(const char* string);
static string GetGitCommand(char* argv[]);
static inline bool IsAlias(const string& gitCommand);
static string GetGitCommandSessionId();

void ReleaseReponseHandler(const string& rawResponse)
{
    size_t headerSeparator = rawResponse.find(MessageSeparator);
    string responseHeader;
    string responseBody;
    if (headerSeparator != string::npos)
    {
        responseHeader = rawResponse.substr(0, headerSeparator);
        responseBody = rawResponse.substr(headerSeparator + 1);
    }
    else
    {
        responseHeader = rawResponse;
    }

    if (!responseBody.empty())
    {
        vector<string> releaseLockSections(String_Split('<', responseBody));
        if (releaseLockSections.size() != 4)
        {
            printf("\nError communicating with GVFS: Run 'git status' to check the status of your repo\n");
            return;
        }

        int failedToUpdateCount = 0;
        try
        {
            failedToUpdateCount = std::stoi(releaseLockSections[0]);
        }
        catch (...)
        {
            printf("\nError communicating with GVFS: Run 'git status' to check the status of your repo\n");
            return;
        }

        int failedToDeleteCount = 0;
        try
        {
            failedToDeleteCount = std::stoi(releaseLockSections[1]);
        }
        catch (...)
        {
            printf("\nError communicating with GVFS: Run 'git status' to check the status of your repo\n");
            return;
        }

        if (failedToUpdateCount > 0 || failedToDeleteCount > 0)
        {
            if (failedToUpdateCount + failedToDeleteCount > 100)
            {
                printf("\nGVFS failed to update %d files, run 'git status' to check the status of files in the repo", failedToDeleteCount + failedToUpdateCount);
            }
            else
            {
                vector<string> failedUpdateList(String_Split('|', releaseLockSections[2]));
                vector<string> failedDeleteList(String_Split('|', releaseLockSections[3]));;
                if (!failedDeleteList.empty())
                {
                    string deleteFailuresMessage = BuildUpdatePlaceholderFailureMessage(failedDeleteList, "delete", "git clean -f ");
                    printf(deleteFailuresMessage.c_str());
                }

                if (!failedUpdateList.empty())
                {
                    string updateFailuresMessage = BuildUpdatePlaceholderFailureMessage(failedUpdateList, "update", "git checkout -- ");
                    printf(updateFailuresMessage.c_str());
                }
            }
        }
    }
}

void SendReleaseLock(
    bool unattended,
    PIPE_HANDLE pipeClient,
    const string& fullCommand,
    int pid,
    bool isElevated,
    bool isConsoleOutputRedirectedToFile)
{
    // Format:
    // "ReleaseLock|<pid>|<is elevated>|<checkAvailabilityOnly>|<parsed command length>|<parsed command>|<gitcommndsessionid length>|<gitcommand sessionid>"

    ostringstream requestMessageStream;
    requestMessageStream
        << "ReleaseLock" << "|"
        << pid << "|"
        << (isElevated ? "true" : "false") << "|"
        << "false" << "|"
        << fullCommand.length() << "|"
        << fullCommand << "|"
        << 0 << "|"
        << ""
        << TerminatorChar;

    std::string requestMessage = requestMessageStream.str();

    unsigned long bytesWritten;
    unsigned long messageLength = static_cast<unsigned long>(requestMessage.length());
    int error = 0;
    bool success = WriteToPipe(
        pipeClient,
        requestMessage.c_str(),
        messageLength,
        &bytesWritten,
        &error);

    if (!success || bytesWritten != messageLength)
    {
        die(ReturnCode::PipeWriteFailed, "Failed to write to pipe (%d)\n", error);
    }

    auto releaseLock = [&pipeClient]()
    {
        string response;
        if (!Messages_ReadTerminatedMessageFromGVFS(pipeClient, /* out */ response))
        {
            printf("\nError communicating with GVFS: Run 'git status' to check the status of your repo\n");
            return true;
        }

        ReleaseReponseHandler(response);
        return true;
    };

    if (unattended || isConsoleOutputRedirectedToFile)
    {
        releaseLock();
    }
    else
    {
        Console_ShowStatusWhileRunning(
            releaseLock,
            "Waiting for GVFS to parse index and update placeholder files",
            !isConsoleOutputRedirectedToFile, // showSpinner
            PostCommandSpinnerDelayMs);
    }
}

int main(int argc, char *argv[])
{
    if (argc < 3)
    {
        ExitWithError("Usage: gvfs.commandhook.exe --git-pid=<pid> <hook> <git verb> [<other arguments>]");
    }

    bool unattended = GVFSEnvironment_IsUnattended();

    if (!GetPipeNameIfInsideGVFSRepo(/*out*/ s_pipeName))
    {
        // TODO (hack): Use ExitWithError instead inside of GetPipeNameIfInsideGVFSRepo
        // Nothing to hook when being run outside of a GVFS repo.
        // This is also the path when run with --git-dir outside of a GVFS directory, see Story #949665
        exit(0);
    }

    DisableCRLFTranslationOnStdPipes();

    HookType hookType = GetHookType(argv[1]);
    switch (hookType)
    {
    case HookType::PreCommand:
        CheckForLegalCommands(argv);
        RunLockRequest(argc, argv, unattended, AcquireGVFSLockForProcess);
        RunPreCommands(argv);
        break;

    case HookType::PostCommand:
        // Do not release the lock if this request was only run to see if it could acquire the GVFSLock,
        // but did not actually acquire it.
        if (!CheckGVFSLockAvailabilityOnly(argc, argv))
        {
            RunLockRequest(argc, argv, unattended, ReleaseGVFSLock);
        }

        RunPostCommands(unattended);
        break;

    default:
        ExitWithError("Unrecognized hook: " + string(argv[1]));
        break;
    }

    return 0;
}

static inline void RunPreCommands(char *argv[])
{
    string command = GetGitCommand(argv);
    if (command == "fetch" || command == "pull")
    {
        Process_Run("gvfs", "prefetch --commits", /*redirectOutput*/ false);
    }
}

static inline void RunPostCommands(bool unattended)
{
    if (!unattended)
    {
        RemindUpgradeAvailable();
    }
}

static inline void RemindUpgradeAvailable()
{
    // The idea is to generate a random number between 0 and 99. To make
    // sure that the reminder is displayed only 10% of the times a git
    // command is run, check that the random number is between 0 and 10,
    // which will have a probability of 10/100 == 10%.
    std::mt19937 gen(static_cast<unsigned int>(std::time(nullptr) % UINT_MAX)); //Standard mersenne_twister_engine seeded with the current time
    const int reminderFrequency = 10;
    int randomValue = gen() % 100;

    if (randomValue <= reminderFrequency && Upgrader_IsLocalUpgradeAvailable())
    {
        printf(ReminderNotification.c_str());
    }
}

static inline void ExitWithError(const std::string& error)
{
    printf("%s\n", error.c_str());
    exit(1);
}

static inline void CheckForLegalCommands(char* argv[])
{
    string command(GetGitCommand(argv));
    if (command == "gui")
    {
        ExitWithError("To access the 'git gui' in a GVFS repo, please invoke 'git-gui.exe' instead.");
    }
}

static void RunLockRequest(int argc, char *argv[], bool unattended, function<void(bool, int, char*[], int, PIPE_HANDLE)> requestToRun)
{
    if (ShouldLock(argc, argv))
    {
        PIPE_HANDLE pipeHandle = CreatePipeToGVFS(s_pipeName);

        int pid = GetParentPid(argc, argv);
        if (pid == InvalidProcessId || !Process_IsProcessActive(pid))
        {
            ExitWithError("GVFS.Hooks: Unable to find parent git.exe process (PID: " + to_string(pid) + ").");
        }

        requestToRun(unattended, argc, argv, pid, pipeHandle);
    }
}

static string GenerateFullCommand(int argc, char* argv[])
{
    string fullGitCommand("git ");
    for (int i = 2; i < argc; ++i)
    {
        if (strlen(argv[i]) < GitPidArg.length())
        {
            fullGitCommand += " ";
            fullGitCommand += argv[i];
        }
        else if (0 != strncmp(argv[i], GitPidArg.c_str(), GitPidArg.length()))
        {
            fullGitCommand += " ";
            fullGitCommand += argv[i];
        }
    }

    return fullGitCommand;
}

static int GetParentPid(int argc, char* argv[])
{
    char** beginArgs = argv;
    char** endArgs = beginArgs + argc;

    char** pidArg = find_if(
        beginArgs,
        endArgs,
        [](char* argString)
    {
        if (strlen(argString) < GitPidArg.length())
        {
            return false;
        }

        return 0 == strncmp(GitPidArg.c_str(), argString, GitPidArg.length());
    });

    if (pidArg != endArgs)
    {
        // TODO (hack): Error on duplicates?
        string pidString(*pidArg);
        if (!pidString.empty())
        {
            pidString = pidString.substr(GitPidArg.length());

            // TODO (hack): Ensure string is value int value?
            return std::atoi(pidString.c_str());
        }

    }

    ExitWithError("Git did not supply the process Id.\nEnsure you are using the correct version of the git client.");

    return InvalidProcessId;
}

static void AcquireGVFSLockForProcess(bool unattended, int argc, char* argv[], int pid, PIPE_HANDLE pipeClient)
{
    string result;
    bool checkGvfsLockAvailabilityOnly = CheckGVFSLockAvailabilityOnly(argc, argv);
    string fullCommand = GenerateFullCommand(argc, argv);
    string gitCommandSessionId = GetGitCommandSessionId();

    if (!GVFSLock_TryAcquireGVFSLockForProcess(
        unattended,
        pipeClient,
        fullCommand,
        pid,
        Process_IsElevated(),
        Process_IsConsoleOutputRedirectedToFile(),
        checkGvfsLockAvailabilityOnly,
        gitCommandSessionId,
        result))
    {
        ExitWithError(result);
    }
}

void ReleaseGVFSLock(bool unattended, int argc, char* argv[], int pid, PIPE_HANDLE pipeClient)
{
    string fullCommand = GenerateFullCommand(argc, argv);

    SendReleaseLock(
        unattended,
        pipeClient,
        fullCommand,
        pid,
        Process_IsElevated(),
        Process_IsConsoleOutputRedirectedToFile());
}

static bool CheckGVFSLockAvailabilityOnly(int argc, char *argv[])
{
    // Don't acquire the GVFS lock if the git command is not acquiring locks.
    // This enables tools to run status commands without to the index and
    // blocking other commands from running. The git argument
    // "--no-optional-locks" results in a 'negative'
    // value GIT_OPTIONAL_LOCKS environment variable.

    if (GetGitCommand(argv) != "status")
    {
        return false;
    }

    char** beginArgs = argv;
    char** endArgs = beginArgs + argc;
    if (endArgs != find_if(beginArgs, endArgs, [](char* argString) { return (0 == _stricmp(argString, "--no-lock-index")); }))
    {
        return true;
    }

    return IsGitEnvVarDisabled("GIT_OPTIONAL_LOCKS");
}

static string BuildUpdatePlaceholderFailureMessage(vector<string>& fileList, const string& failedOperation, const string& recoveryCommand)
{
    // TODO (hack): Test this with non-ascii file names
    struct
    {
        bool operator()(const string& a, const string& b) const
        {
            return _stricmp(a.c_str(), b.c_str()) < 0;
        }
    } caseInsensitiveCompare;

    sort(fileList.begin(), fileList.end(), caseInsensitiveCompare);

    ostringstream message;
    message << "\nGVFS was unable to " << failedOperation << " the following files. To recover, close all handles to the files and run these commands:";
    for (const string& file : fileList)
    {
        message << "\n    " << recoveryCommand << file;
    }

    return message.str();
}

static bool IsGitEnvVarDisabled(const string& envVar)
{
    char gitEnvVariable[2056];
    size_t requiredSize;

    // TOOD (hack): handle error codes
    if (getenv_s(&requiredSize, gitEnvVariable, envVar.c_str()) == 0)
    {
        if (_stricmp(gitEnvVariable, "false") == 0 ||
            _stricmp(gitEnvVariable, "no") == 0 ||
            _stricmp(gitEnvVariable, "off") == 0 ||
            _stricmp(gitEnvVariable, "0") == 0)
        {
            return true;
        }
    }

    return false;
}

static bool ShouldLock(int argc, char* argv[])
{
    string gitCommand(GetGitCommand(argv));

    switch (gitCommand[0])
    {
    // Keep these alphabetically sorted
    case 'b':
        if (gitCommand == "blame" ||
            gitCommand == "branch")
        {
            return false;
        }

        break;

    case 'c':
        if (gitCommand == "cat-file" ||
            gitCommand == "check-attr" ||
            gitCommand == "check-ignore" ||
            gitCommand == "check-mailmap" ||
            gitCommand == "commit-graph" ||
            gitCommand == "config" ||
            gitCommand == "credential")
        {
            return false;
        }
        break;

    case 'd':
        if (gitCommand == "diff" ||
            gitCommand == "diff-files" ||
            gitCommand == "diff-index" ||
            gitCommand == "diff-tree" ||
            gitCommand == "difftool")
        {
            return false;
        }

        break;

    case 'f':
        if (gitCommand == "fetch" ||
            gitCommand == "for-each-ref")
        {
            return false;
        }

        break;

    case 'h':
        if (gitCommand == "help" ||
            gitCommand == "hash-object")
        {
            return false;
        }

        break;

    case 'i':
        if (gitCommand == "index-pack")
        {
            return false;
        }

        break;

    case 'l':
        if (gitCommand == "log" ||
            gitCommand == "ls-files" ||
            gitCommand == "ls-tree")
        {
            return false;
        }

        break;

    case 'm':
        if (gitCommand == "merge-base" ||
            gitCommand == "multi-pack-index")
        {
            return false;
        }

        break;

    case 'n':
        if (gitCommand == "name-rev")
        {
            return false;
        }

        break;

    case 'p':
        if (gitCommand == "push")
        {
            return false;
        }

        break;

    case 'r':
        if (gitCommand == "remote" ||
            gitCommand == "rev-list" ||
            gitCommand == "rev-parse")
        {
            return false;
        }

        break;

    case 's':
        /*
         * There are several git commands that are "unsupoorted" in virtualized (VFS4G)
         * enlistments that are blocked by git. Usually, these are blocked before they acquire
         * a GVFSLock, but the submodule command is different, and is blocked after acquiring the
         * GVFS lock. This can cause issues if another action is attempting to create placeholders.
         * As we know the submodule command is a no-op, allow it to proceed without acquiring the
         * GVFSLock. I have filed issue #1164 to track having git block all unsupported commands
         * before calling the pre-command hook.
         */
        if (gitCommand == "show" ||
            gitCommand == "show-ref" ||
            gitCommand == "symbolic-ref" ||
            gitCommand == "submodule")
        {
            return false;
        }

        break;

    case 't':
        if (gitCommand == "tag")
        {
            return false;
        }

        break;

    case 'u':
        if (gitCommand == "unpack-objects" ||
            gitCommand == "update-ref")
        {
            return false;
        }

        break;

    case 'v':
        if (gitCommand == "version")
        {
            return false;
        }

        break;

    case 'w':
        if (gitCommand == "web--browse")
        {
            return false;
        }

        break;

    default:
        break;
    }

    char** beginArgs = argv;
    char** endArgs = beginArgs + argc;

    if (gitCommand == "reset")
    {
        if (endArgs != find_if(beginArgs, endArgs, [](char* argString) { return (0 == strcmp(argString, "--soft")); }))
        {
            return false;
        }
    }

    if (!KnownGitCommands_CommandIsKnown(gitCommand) && IsAlias(gitCommand))
    {
        return false;
    }

    return true;
}

static inline HookType GetHookType(const char* string)
{
    if (string == PreCommandHook)
    {
        return HookType::PreCommand;
    }
    else if (string == PostCommandHook)
    {
        return HookType::PostCommand;
    }

    return HookType::Invalid;
}

static string GetGitCommand(char* argv[])
{
    string command(argv[2]);
    transform(
        command.begin(), 
        command.end(), 
        command.begin(), 
        [](unsigned char c) { return static_cast<unsigned char>(std::tolower(c)); });

    if (command.length() >= 4 && command.substr(0, 4) == "git-")
    {
        command = command.substr(4);
    }

    return command;
}

static inline bool IsAlias(const string& gitCommand)
{
    string output = Process_Run("git", "config --get alias." + gitCommand, /*redirectOutput*/ true);
    return !output.empty();
}

static string GetGitCommandSessionId()
{
    char gitEnvVariable[2056];
    size_t requiredSize;

    // TOOD (hack): handle error codes
    if (getenv_s(&requiredSize, gitEnvVariable, "GIT_TR2_PARENT_SID") == 0)
    {
        return gitEnvVariable;
    }

    return "";
}