#pragma once

inline std::vector<std::string> String_Split(char deliminator, const std::string toSplit)
{
    std::vector<std::string> parts;
    size_t offset = 0;
    size_t delimPos = toSplit.find(deliminator);
    while (delimPos != std::string::npos)
    {
        parts.emplace_back(toSplit.substr(offset, delimPos - offset));
        offset = delimPos + 1;
        delimPos = toSplit.find(deliminator, offset);
    }

    parts.emplace_back(toSplit.substr(offset));

    return parts;
}