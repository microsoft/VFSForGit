#pragma once

#include "Should.h"
#include "TestHelpers.h"

namespace TestVerifiers
{

inline void ExpectDirEntries(const std::string& path, std::vector<std::string>& entries)
{
    std::vector<FileInfo> result = TestHelpers::EnumDirectory(path);
    entries.push_back(".");
    entries.push_back("..");

    VERIFY_ARE_EQUAL(entries.size(), result.size());

    for (const std::string& entry : entries) 
    {
        bool found = false;
        for (std::vector<FileInfo>::iterator resultIt = result.begin(); resultIt != result.end(); resultIt++)
        {
            if (resultIt->Name == entry) 
            {
                result.erase(resultIt);
                found = true;
                break;
            }
        }

        if (!found) 
        {
            VERIFY_FAIL(("  [" + entry + "] not found").c_str());
            return;
        }
    }

    if (!result.empty()) 
    {
        VERIFY_FAIL("Some expected results not found");
    }
}

inline void AreEqual(const std::string& str1, const std::string& str2)
{
    VERIFY_ARE_EQUAL(str1, str2);
}

} // namespace TestVerifiers