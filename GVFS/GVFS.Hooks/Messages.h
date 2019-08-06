#pragma once
#include "common.h"

const char MessageSeparator = '|';

bool Messages_ReadTerminatedMessageFromGVFS(PIPE_HANDLE pipeHandle, std::string& responseMessage);