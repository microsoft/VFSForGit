#include <copyfile.h>
#include <EndpointSecurity/EndpointSecurity.h>
#include <cstdio>
#include <dispatch/dispatch.h>
#include <cstring>
#include <fcntl.h>
#include <sys/stat.h>
#include <sys/errno.h>
#include <string>
#include <cstdlib>
#include <mutex>
#include <unordered_map>
#include <vector>
#include <bsm/libbsm.h>

using std::string;
using std::mutex;
using std::unordered_map;
using std::vector;

typedef std::lock_guard<mutex> Guard;

static constexpr const char* EmptyFileXattr = "org.vfsforgit.endpointsecuritymirror.emptyfile";

static es_client_t* client = nullptr;
static dispatch_queue_t s_hydrationQueue = nullptr;
static mutex s_hydrationMutex;
static unordered_map<string, vector<es_message_t*>> s_waitingFileHydrationMessages;

static void HandleSecurityEvent(
	es_client_t* _Nonnull client, const es_message_t* _Nonnull message);
static int RecursiveEnumerationCopyfileStatusCallback(
	int what, int stage, copyfile_state_t state, const char * src, const char * dst, void * ctx);

static string s_sourcePrefix, s_targetPrefix;

// Helper function that automatically fills out the event_count in es_subscribe calls
template <typename... ARGS>
bool ESSubscribe(es_client_t* _Nonnull client, ARGS... events)
{
  return es_subscribe(client, sizeof...(events), events...);
}

static const char* FilenameFromPath(const char* path)
{
	const char* lastSlash = std::strrchr(path, '/');
	if (lastSlash == nullptr)
	{
		return path;
	}
	else
	{
		return lastSlash + 1;
	}
}

int main(int argc, const char* argv[])
{
	if (argc < 3)
	{
		fprintf(stderr, "Run as: %s <source directory> <target directory>\n", FilenameFromPath(argv[0]));
		return 1;
	}
	
	s_hydrationQueue = dispatch_queue_create("org.vfsforgit.endpointsecuritymirror.hydrationqueue", DISPATCH_QUEUE_CONCURRENT);
	
	const char* const sourceDir = argv[1];
	struct stat sourceDirStat = {};
	if (0 != stat(sourceDir, &sourceDirStat))
	{
		perror("stat() on source directory failed");
		return 1;
	}
	
	if (!S_ISDIR(sourceDirStat.st_mode))
	{
		fprintf(stderr, "Source (%s) is not a directory.\n", sourceDir);
		return 1;
	}


	es_new_client_result_t result = es_new_client(
		&client,
		^(es_client_t* _Nonnull client, const es_message_t* _Nonnull message)
		{
			HandleSecurityEvent(client, message);
		});
	if (result != ES_NEW_CLIENT_RESULT_SUCCESS)
	{
		fprintf(stderr, "es_new_client failed, error = %u\n", result);
		return 1;
	}


	const char* const targetDir = argv[2];
	int targetDirFD = open(targetDir, O_DIRECTORY | O_RDONLY);
	if (targetDirFD < 0)
	{
		if (errno == ENOENT)
		{
			// target directory does not exist, create it and enumerate
			copyfile_state_t copyState = copyfile_state_alloc();
			copyfile_state_set(copyState, COPYFILE_STATE_STATUS_CB, reinterpret_cast<void*>(&RecursiveEnumerationCopyfileStatusCallback));
			int result = copyfile(sourceDir, targetDir, copyState, COPYFILE_METADATA | COPYFILE_RECURSIVE);
			if (result != 0)
			{
				perror("copyfile() for enumeration failed");
			}
		}
		else
		{
			perror("open() on target directory failed");
			return 1;
		}
	}
	
	char* absoluteSourceDir = realpath(sourceDir, nullptr);
	s_sourcePrefix = absoluteSourceDir;
	free(absoluteSourceDir);
	if (s_sourcePrefix[s_sourcePrefix.length() - 1] != '/')
		s_sourcePrefix.append("/");
	
	char* absoluteTargetDir = realpath(targetDir, nullptr);
	s_targetPrefix = absoluteTargetDir;
	free(absoluteTargetDir);
	if (s_targetPrefix[s_targetPrefix.length() - 1] != '/')
		s_targetPrefix.append("/");


	printf("Starting up with source '%s', target '%s'\n", s_sourcePrefix.c_str(), s_targetPrefix.c_str());
	es_clear_cache(client);
	
	
	if (!ESSubscribe(client, ES_EVENT_TYPE_AUTH_OPEN))
	{
		fprintf(stderr, "es_subscribe failed\n");
		return 1;
	}
	
	dispatch_main();
}


static bool PathLiesWithinTarget(const char* path)
{
	return 0 == strncmp(s_targetPrefix.c_str(), path, s_targetPrefix.length());
}

static string SourcePathForTargetFile(const char* targetPath)
{
	return s_sourcePrefix + (targetPath + s_targetPrefix.length());
}

static void HydrateFileOrAwaitHydration(string eventPath, const es_message_t* message);

static void HandleSecurityEvent(
	es_client_t* _Nonnull client, const es_message_t* _Nonnull message)
{
	if (message->action_type == ES_ACTION_TYPE_AUTH)
	{
		if (message->event_type == ES_EVENT_TYPE_AUTH_OPEN)
		{
			char xattrBuffer[16];
			const char* eventPath = message->event.open.file.path;
			ssize_t xattrBytes = getxattr(eventPath, EmptyFileXattr, xattrBuffer, sizeof(xattrBuffer), 0 /* offset */, 0 /* options */);
			if (xattrBytes >= 0)
			{
				if (PathLiesWithinTarget(eventPath))
				{
					const char* processFilename = FilenameFromPath(message->proc.file.path);
					if (0 == strcmp("mdworker_shared", processFilename))
					{
						printf("Denying crawler process %u (%s) access to empty file '%s'\n", audit_token_to_pid(message->proc.audit_token), processFilename, eventPath);
						es_respond_flags_result(client, message, 0x0, false /* don't cache */);
					}
					else
					{
						HydrateFileOrAwaitHydration(eventPath, message);
					}
					return;
				}
				else
				{
					fprintf(stderr, "File tagged as empty found outside target directory: '%s'\n", eventPath);
					es_respond_flags_result(client, message, 0x0, false /* don't cache */);
					return;
				}
			}
			else if (errno != ENOATTR)
			{
				fprintf(stderr, "getxattr failed (%u, %s) on '%s' (within mirror target directory: %s)\n",
					errno, strerror(errno), eventPath, PathLiesWithinTarget(eventPath) ? "YES" : "NO");
			}
		
			es_respond_flags_result(client, message, 0x7fffffff, false /* don't cache */);
		}
        else
        {
            fprintf(stderr, "Unexpected event type: %u\n", message->event_type);
        }
	}
    else
    {
        fprintf(stderr, "Unexpected action type: %u, event type: %u\n", message->action_type, message->event_type);
    }
}

static void HydrateFileOrAwaitHydration(string eventPath, const es_message_t* message)
{
	es_message_t* messageCopy = es_copy_message(message);
	Guard lock(s_hydrationMutex);
	if (!s_waitingFileHydrationMessages.insert(make_pair(eventPath, vector<es_message_t*>())).second)
	{
		// already being hydrated, add to messages needing approval
		s_waitingFileHydrationMessages[eventPath].push_back(messageCopy);
	}
	else
	{
		dispatch_async(s_hydrationQueue, ^{
			char xattrBuffer[16];
			ssize_t xattrBytes = getxattr(eventPath.c_str(), EmptyFileXattr, xattrBuffer, sizeof(xattrBuffer), 0 /* offset */, 0 /* options */);
			if (xattrBytes < 0)
			{
				// Raced with other thread, hydration already done
				es_respond_flags_result(client, messageCopy, 0x7fffffff, false /* don't cache */);
				return;
			}

			string sourcePath = SourcePathForTargetFile(eventPath.c_str());
			// NOTE: if you increase this beyond 60000ms (1 minute) the process ends up being killed
			unsigned delay_ms = random() % 60000u;
			printf("Hydrating '%s' -> '%s' for process %u (%s), with %u ms delay\n", sourcePath.c_str(), eventPath.c_str(), audit_token_to_pid(messageCopy->proc.audit_token), FilenameFromPath(messageCopy->proc.file.path), delay_ms);
			usleep(delay_ms * 1000u);
			int result = copyfile(sourcePath.c_str(), eventPath.c_str(), nullptr /* state */, COPYFILE_DATA);
			
			uint32_t responseFlags = 0x7fffffff;
			if (result == 0)
			{
				result = removexattr(eventPath.c_str(), EmptyFileXattr, 0 /* options */);
				if (result != 0)
				{
					perror("removexattr failed");
				}
				else
				{
					printf("Hydrating '%s' done\n", eventPath.c_str());
				}
			}
			else
			{
				perror("hydration copyfile failed");
				responseFlags = 0x0;
			}
			
			es_respond_flags_result(client, messageCopy, responseFlags, false /* don't cache */);
			free(messageCopy);
			
			vector<es_message_t*> waitingMessages;
			
			{
				Guard lock(s_hydrationMutex);
			 	s_waitingFileHydrationMessages[eventPath].swap(waitingMessages);
			 	s_waitingFileHydrationMessages.erase(eventPath);
			}
			
			while (!waitingMessages.empty())
			{
				es_message_t* waitingMessage = waitingMessages.back();
				waitingMessages.pop_back();
				
				es_respond_flags_result(client, waitingMessage, responseFlags, false /* don't cache */);
				free(waitingMessage);
			}
		});
	}
}

static const char* CopyfileWhatString(int what)
{
	switch (what)
	{
  case COPYFILE_RECURSE_FILE:
		return "COPYFILE_RECURSE_FILE";
	case COPYFILE_RECURSE_DIR:
		return "COPYFILE_RECURSE_DIR";
	case COPYFILE_RECURSE_DIR_CLEANUP:
		return "COPYFILE_RECURSE_DIR_CLEANUP";
	case COPYFILE_RECURSE_ERROR:
		return "COPYFILE_RECURSE_ERROR";
  default:
    return "???";
	}
}

static const char* CopyfileStageString(int stage)
{
	switch (stage)
	{
  case COPYFILE_START:
    return "COPYFILE_START";
	case COPYFILE_FINISH:
		return "COPYFILE_FINISH";
	case COPYFILE_ERR:
		return "COPYFILE_ERR";
  default:
    return "???";
	}
}

static int RecursiveEnumerationCopyfileStatusCallback(
	int what, int stage, copyfile_state_t state, const char* src, const char* dst, void* ctx)
{
	printf("copyfile callback: what = %s, stage = %s, src = '%s', dst = '%s'\n", CopyfileWhatString(what), CopyfileStageString(stage), src, dst);
	
	if (what == COPYFILE_RECURSE_FILE && stage == COPYFILE_FINISH)
	{
		struct stat destStat = {};
		if (0 != stat(dst, &destStat))
		{
			perror("stat() on destination failed");
		}
		else
		{
			if (0 != setxattr(dst, EmptyFileXattr, "", 0 /* size */, 0 /* offset */, 0 /* options */))
			{
				perror("setxattr failed: ");
			}
		}
	}
	return COPYFILE_CONTINUE;
}

