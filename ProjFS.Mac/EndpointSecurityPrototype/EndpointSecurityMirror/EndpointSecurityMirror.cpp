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

using std::string;

static constexpr const char* EmptyFileXattr = "org.vfsforgit.endpointsecuritymirror.emptyfile";

static es_client_t* client = nullptr;

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
			/*
			if (0 != mkdir(targetDir, sourceDirStat.st_mode))
			{
				perror("mkdir() of target failed");
				return 1;
			}
			if (0 != chown(targetDir, sourceDirStat.st_uid, sourceDirStat.st_gid))
			{
				perror("chown() of target failed");
				return 1;
			}
			 */

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
					string sourcePath = SourcePathForTargetFile(eventPath);
					
					printf("Hydrating '%s' -> '%s'\n", sourcePath.c_str(), eventPath);
					int result = copyfile(sourcePath.c_str(), eventPath, nullptr /* state */, COPYFILE_DATA);
					if (result == 0)
					{
						result = removexattr(eventPath, EmptyFileXattr, 0 /* options */);
						if (result != 0)
						{
							perror("removexattr failed");
						}
					}
					else
					{
						perror("hydration copyfile failed");
						es_respond_flags_result(client, message, 0x0, false /* don't cache */);
						return;
					}
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
				perror("getxattr failed");
			}
		
			es_respond_flags_result(client, message, 0x7fffffff, false /* don't cache */);
		}
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

