#pragma once

bool Process_IsElevated();

// TODO (hack): Use PATH_STRING
std::string Process_Run(const std::string& processName, const std::string& args, bool redirectOutput);

bool Process_IsConsoleOutputRedirectedToFile();
bool Process_IsProcessActive(int pid);
