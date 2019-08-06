#pragma once

bool Console_ShowStatusWhileRunning(
    std::function<bool()> action,
    const std::string& message,
    bool showSpinner,
    int initialDelayMs);