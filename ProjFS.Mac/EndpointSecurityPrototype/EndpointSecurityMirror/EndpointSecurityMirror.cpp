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
#include <atomic>
#include <mach/mach_time.h>

using std::string;
using std::mutex;
using std::unordered_map;
using std::vector;
using std::atomic_uint;
using std::extent;

typedef std::lock_guard<mutex> Guard;

// Local function declarations

static void HandleSecurityEvent(
	es_client_t* _Nonnull client, const es_message_t* _Nonnull message);
static int RecursiveEnumerationCopyfileStatusCallback(
	int what, int stage, copyfile_state_t state, const char * src, const char * dst, void * ctx);
static void HydrateFileOrAwaitHydration(string eventPath, const es_message_t* message);
static void HydrateFile(string eventPath, es_message_t* message);
static const char* CopyfileWhatString(int what) __attribute__((unused));
static const char* CopyfileStageString(int stage) __attribute__((unused));

// Global/static variable definitions

static pid_t selfpid;
static constexpr const char* EmptyFileXattr = "org.vfsforgit.endpointsecuritymirror.emptyfile";

static es_client_t* client = nullptr;
static dispatch_queue_t s_hydrationQueue = nullptr;
static mutex s_hydrationMutex;
static unordered_map<string, vector<es_message_t*>> s_waitingFileHydrationMessages;

static string s_sourcePrefix, s_targetPrefix;

static atomic_uint s_pendingAuthCount(0);
static mach_timebase_info_data_t s_machTimebase;

// Helper functions

static uint64_t usecFromMachDuration(uint64_t machDuration)
{
	// timebase gives ns, divide by 1000 for usec
	return ((machDuration * s_machTimebase.numer) / s_machTimebase.denom) / 1000u;
}

static int64_t usecFromMachDuration(int64_t machDuration)
{
	return ((machDuration * s_machTimebase.numer) / s_machTimebase.denom) / 1000;
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

static bool PathLiesWithinTarget(const char* path)
{
	return 0 == strncmp(s_targetPrefix.c_str(), path, s_targetPrefix.length());
}

static string SourcePathForTargetFile(const char* targetPath)
{
	return s_sourcePrefix + (targetPath + s_targetPrefix.length());
}


// Helper function for making "what" argument to copyfile callback human-readable
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

// Helper function for making "stage" argument to copyfile callback human-readable
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

//

int main(int argc, const char* argv[])
{
	selfpid = getpid();
	mach_timebase_info(&s_machTimebase); // required for subsequent mach time <-> nsec/usec conversions
	
	if (argc < 3)
	{
		fprintf(stderr, "Run as: %s <source directory> <target directory>\n", FilenameFromPath(argv[0]));
		return 1;
	}
	
	// The dispatch queue used for processing hydration requests.
	// Note: concurrent, i.e. multithreaded.
	s_hydrationQueue = dispatch_queue_create("org.vfsforgit.endpointsecuritymirror.hydrationqueue", DISPATCH_QUEUE_CONCURRENT);
	
	// Sanity check on the mirror source: must exist, must be a directory
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

	// If the target directory exists, assume it has already been populated with
	// placeholders. If not, perform recursive enumeration.
	const char* const targetDir = argv[2];
	int targetDirFD = open(targetDir, O_DIRECTORY | O_RDONLY);
	if (targetDirFD < 0)
	{
		if (errno == ENOENT)
		{
			copyfile_state_t copyState = copyfile_state_alloc();
			// This callback function will be called multiple times on every directory
			// and file encountered during recursive descent into the source directory
			// Check copyfile manpage for details.
			// We use the callback to set the EmptyFileXattr xattr and truncate
			// the file to the correct length.
			copyfile_state_set(copyState, COPYFILE_STATE_STATUS_CB, reinterpret_cast<void*>(&RecursiveEnumerationCopyfileStatusCallback));
			// Note that the COPYFILE_DATA flag is NOT set; this would copy the file
			// contents, but we want empty placeholders.
			int result = copyfile(sourceDir, targetDir, copyState, COPYFILE_METADATA | COPYFILE_RECURSIVE | COPYFILE_NOFOLLOW_SRC | COPYFILE_NOFOLLOW_DST);
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
	else
	{
		close(targetDirFD);
	}
	
	// Ensure we have paths with trailing slashes for both source and target, so
	// that simple string prefix tests will suffice from here on out.
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

	// Perform the EndpointSecurity start-up:
	// Create client object, clear cache, and subscribe to events we're interested in.
	es_new_client_result_t result = es_new_client(
		&client,
		^(es_client_t* _Nonnull client, const es_message_t* _Nonnull message)
		{
			std::atomic_fetch_add(&s_pendingAuthCount, 1u);
			HandleSecurityEvent(client, message);
		});
	if (result != ES_NEW_CLIENT_RESULT_SUCCESS)
	{
		fprintf(stderr, "es_new_client failed, error = %u\n", result);
		return 1;
	}

	es_clear_cache(client); // This may no longer be necessary; without it, early macOS 10.15 betas would drop events.
	
	es_event_type_t subscribe_events[] = { ES_EVENT_TYPE_AUTH_OPEN, ES_EVENT_TYPE_NOTIFY_LOOKUP };
	if (ES_RETURN_SUCCESS != es_subscribe(client, subscribe_events, extent<decltype(subscribe_events)>::value))
	{
		fprintf(stderr, "es_subscribe failed\n");
		return 1;
	}
	
	// Handle events until process is killed
	dispatch_main();
}

static void HandleSecurityEvent(
	es_client_t* _Nonnull client, const es_message_t* _Nonnull message)
{
	if (message->action_type == ES_ACTION_TYPE_AUTH)
	{
		if (message->event_type == ES_EVENT_TYPE_AUTH_OPEN)
		{
			pid_t pid = audit_token_to_pid(message->process->audit_token);
			if (pid == selfpid)
			{
				printf("Muting events from self (pid %d)\n", pid);
				es_mute_process(client, &message->process->audit_token);
				es_respond_result_t result = es_respond_flags_result(client, message, 0x7fffffff, false /* don't cache */);
				assert(result == ES_RESPOND_RESULT_SUCCESS);
				std::atomic_fetch_sub(&s_pendingAuthCount, 1u);
				return;
			}
			
			const char* eventPath = message->event.open.file->path.data;
			if (PathLiesWithinTarget(eventPath))
			{
				char xattrBuffer[16];
				ssize_t xattrBytes = getxattr(eventPath, EmptyFileXattr, xattrBuffer, sizeof(xattrBuffer), 0 /* offset */, 0 /* options */);
				if (xattrBytes >= 0)
				{
					// If we end up here, the event path lies within the mirror target,
					// and the file is empty and thus needs hydrating before the open()
					// call may proceed.
				
					const char* processFilename =
						(message->process && message->process->executable && message->process->executable->path.data)
						? FilenameFromPath(message->process->executable->path.data)
						: nullptr;
					if (processFilename != nullptr &&
							(0 == strcmp("mdworker_shared", processFilename)
					     || 0 == strcmp("mds", processFilename)))
					{
						// Don't allow crawler processes to hydrate, so fail the open() call.
						
						//printf("Denying crawler process %u (%s) access to empty file '%s'\n", audit_token_to_pid(message->proc.audit_token), processFilename, eventPath);
						es_respond_result_t result = es_respond_flags_result(client, message, 0x0, false /* don't cache */);
						assert(result == ES_RESPOND_RESULT_SUCCESS);
						unsigned count = std::atomic_fetch_sub(&s_pendingAuthCount, 1u);
						if (count != 1)
						{
							printf("In-flight authorisation requests pending: %u\n", count - 1);
						}
					}
					else
					{
						// Request hydration
						printf("Hydration event to '%s' caused by '%s'\n", eventPath, processFilename);
						HydrateFileOrAwaitHydration(eventPath, message);
					}
					return;
				}
				else
				{
					// File already hydrated
					es_respond_result_t result = es_respond_flags_result(client, message, 0x7fffffff, false /* don't cache */);
					assert(result == ES_RESPOND_RESULT_SUCCESS);
					unsigned count = std::atomic_fetch_sub(&s_pendingAuthCount, 1u);
					if (count != 1)
					{
						printf("In-flight authorisation requests pending: %u\n", count - 1);
					}
					return;
				}
			}
			else if (errno != ENOATTR && PathLiesWithinTarget(eventPath))
			{
				fprintf(stderr, "getxattr failed (%u, %s) on '%s' (within mirror target directory)\n",
					errno, strerror(errno), eventPath);
			}
		
			unsigned count = std::atomic_fetch_sub(&s_pendingAuthCount, 1u);
			if (count != 1)
			{
				printf("In-flight authorisation requests pending: %u\n", count - 1);
			}
			es_respond_result_t result = es_respond_flags_result(client, message, 0x7fffffff, false /* don't cache */);
			assert(result == ES_RESPOND_RESULT_SUCCESS);
		}
		else
		{
			fprintf(stderr, "Unexpected event type: %u\n", message->event_type);
		}
	}
	else if (message->action_type == ES_ACTION_TYPE_NOTIFY)
	{
		if (message->event_type == ES_EVENT_TYPE_NOTIFY_LOOKUP)
		{
			pid_t pid = audit_token_to_pid(message->process->audit_token);
			if (pid != selfpid)
			{
				const char* eventPath = message->event.lookup.source_dir->path.data;
				if (PathLiesWithinTarget(eventPath))
				{
					printf("ES_EVENT_TYPE_NOTIFY_LOOKUP event for item '%s' in path '%s'\n", message->event.lookup.relative_target.data, eventPath);
				}
			}
			
			std::atomic_fetch_sub(&s_pendingAuthCount, 1u);
			return;
		}
	}
	else
	{
		printf("Unexpected action type: %u, event type: %u\n", message->action_type, message->event_type);
		unsigned count = std::atomic_fetch_sub(&s_pendingAuthCount, 1u);
		if (count != 1)
		{
			printf("In-flight authorisation requests pending: %u\n", count - 1);
		}
	}
}

static void HydrateFileOrAwaitHydration(string eventPath, const es_message_t* message)
{
	es_message_t* messageCopy = es_copy_message(message);
	Guard lock(s_hydrationMutex);
	if (!s_waitingFileHydrationMessages.insert(make_pair(eventPath, vector<es_message_t*>())).second)
	{
		// already being hydrated, add to messages needing approval
		printf("File '%s' already being hydrated by another thread\n", eventPath.c_str());
		s_waitingFileHydrationMessages[eventPath].push_back(messageCopy);
	}
	else
	{
		dispatch_async(s_hydrationQueue, ^{
			HydrateFile(eventPath, messageCopy);
		});
	}
}
			
static void HydrateFile(string eventPath, es_message_t* messageCopy)
{
	// Re-check xattr, file might already be hydrated. (defend against TOCTOU)
			char xattrBuffer[16];
			ssize_t xattrBytes = getxattr(eventPath.c_str(), EmptyFileXattr, xattrBuffer, sizeof(xattrBuffer), 0 /* offset */, 0 /* options */);
			if (xattrBytes < 0)
			{
				// Raced with other thread, hydration already done
				unsigned count = std::atomic_fetch_sub(&s_pendingAuthCount, 1u);
				if (count != 1)
				{
					printf("In-flight authorisation requests pending: %u\n", count - 1);
				}
				es_respond_result_t result = es_respond_flags_result(client, messageCopy, 0x7fffffff, false /* don't cache */);
				assert(result == ES_RESPOND_RESULT_SUCCESS);
				return;
			}

			string sourcePath = SourcePathForTargetFile(eventPath.c_str());
			
			// This can be used for simulating slow hydrations:
			// NOTE: if you increase this beyond 60000ms (1 minute) the process ends up being killed
			unsigned delay_ms = 0;//random() % 60000u;
			//printf("Hydrating '%s' -> '%s' for process %u (%s), with %u ms delay\n", sourcePath.c_str(), eventPath.c_str(), audit_token_to_pid(messageCopy->proc.audit_token), FilenameFromPath(messageCopy->proc.file.path.data), delay_ms);
			usleep(delay_ms * 1000u);
			
			// Enumeration copied metadata, hydration copies data
			int result = copyfile(sourcePath.c_str(), eventPath.c_str(), nullptr /* state */, COPYFILE_DATA);
			
			// This bitfield is required for responding to the open() authorisation.
			// Not 100% sure what the bits stand for, but they are probably the
			// FREAD/FWRITE flags, and the various O_* constants you can pass to open()
			// Note that this value may be cached, so we need to authorise any option
			// bits we might want to allow even if they are not requested for this
			// specific open() call (in messageCopy->event.open.fflag)
			uint32_t responseFlags = 0x7fffffff;
			if (result == 0)
			{
				result = removexattr(eventPath.c_str(), EmptyFileXattr, 0 /* options */);
				if (result != 0)
				{
					perror("removexattr failed");
				}
			}
			else
			{
				perror("hydration copyfile failed");
				responseFlags = 0x0; // This means deny the open() call
			}
			
			// Sanity counter we use to ensure every auth callback is matched by a es_respond_flags_result()
			unsigned count = std::atomic_fetch_sub(&s_pendingAuthCount, 1u);
			if (count != 1)
			{
				// This will occasionally print something when there are multiple callbacks outstanding.
				// No indication of problems if the actual count keeps hovering around 1.
				printf("In-flight authorisation requests pending: %u\n", count - 1);
			}
			
			// Tell ES to allow the open() call where this hydration request originated.
			es_respond_result_t response_result = es_respond_flags_result(client, messageCopy, responseFlags, false /* don't cache */);
			assert(response_result == ES_RESPOND_RESULT_SUCCESS);
			
			// Calculation of remaining time to deadline (so we can see how close we are to having our process killed)
			uint64_t responseMachTime = mach_absolute_time();
			uint64_t responseMachDuration = responseMachTime - messageCopy->mach_time;
			int64_t responseMachDeadlineDelta = responseMachTime - messageCopy->deadline;
	
			printf("Hydrating '%s' done; response took %llu µs, %lld µs %s deadline; triggered by access from process %d ('%s')\n",
				eventPath.c_str(),
				usecFromMachDuration(responseMachDuration),
				std::abs(usecFromMachDuration(responseMachDeadlineDelta)),
				responseMachDeadlineDelta <= 0 ? "before" : "after",
				audit_token_to_pid(messageCopy->process->audit_token),
				messageCopy->process ? messageCopy->process->executable->path.data : "[NULL]");

			es_free_message(messageCopy);
			
			
			// Now respond in the same way to any other open() calls waiting for this file to be hydrated.
			vector<es_message_t*> waitingMessages;
			
			{
				// Atomically remove the list of waiting messages from the global map indexed by filename
				Guard lock(s_hydrationMutex);
			 	s_waitingFileHydrationMessages[eventPath].swap(waitingMessages);
			 	s_waitingFileHydrationMessages.erase(eventPath);
			}
			
			if (!waitingMessages.empty())
				printf("Responding to %zu other auth events for file '%s'\n", waitingMessages.size(), eventPath.c_str());

			while (!waitingMessages.empty())
			{
				es_message_t* waitingMessage = waitingMessages.back();
				waitingMessages.pop_back();
				
				es_respond_result_t response_result = es_respond_flags_result(client, waitingMessage, responseFlags, false /* don't cache */);
				assert(response_result == ES_RESPOND_RESULT_SUCCESS);
				es_free_message(waitingMessage);
				
				unsigned count = std::atomic_fetch_sub(&s_pendingAuthCount, 1u);
				if (count != 1)
				{
					printf("In-flight authorisation requests pending: %u\n", count - 1);
				}
			}
}

static int RecursiveEnumerationCopyfileStatusCallback(
	int what, int stage, copyfile_state_t state, const char* src, const char* dst, void* ctx)
{
	//printf("copyfile callback: what = %s, stage = %s, src = '%s', dst = '%s'\n", CopyfileWhatString(what), CopyfileStageString(stage), src, dst);
	
	if (what == COPYFILE_RECURSE_FILE && stage == COPYFILE_FINISH)
	{
		struct stat destStat = {};
		struct stat srcStat = {};
		if (0 != fstatat(AT_FDCWD, src, &srcStat, AT_SYMLINK_NOFOLLOW))
		{
			fprintf(stderr, "stat() on source file '%s' failed: %d, %s\n", src, errno, strerror(errno));
		}
		else if (0 != fstatat(AT_FDCWD, dst, &destStat, AT_SYMLINK_NOFOLLOW))
		{
			fprintf(stderr, "stat() on destination file '%s' failed: %d, %s\n", dst, errno, strerror(errno));
		}
		else if (S_ISREG(srcStat.st_mode))
		{
			if (0 != setxattr(dst, EmptyFileXattr, "", 0 /* size */, 0 /* offset */, 0 /* options */))
			{
				perror("setxattr failed: ");
			}
			//printf("truncate('%s', %llu)\n", dst, srcStat.st_size);
			if (0 != truncate(dst, srcStat.st_size))
			{
				fprintf(stderr, "truncate() on '%s', %llu bytes failed: %d, %s\n", src, srcStat.st_size, errno, strerror(errno));
			}
		}
	}
	return COPYFILE_CONTINUE;
}

