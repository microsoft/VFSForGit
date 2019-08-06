#include "stdafx.h"
#include "Console.h"

using std::array;
using std::async;
using std::atomic;
using std::chrono::milliseconds;
using std::function;
using std::future;
using std::future_status;
using std::launch;
using std::promise;
using std::string;

bool Console_ShowStatusWhileRunning(
    function<bool()> action,
    const string& message,
    bool showSpinner,
    int initialDelayMs)
{
    bool result = false;
    atomic<bool> initialMessageWritten = false;

    if (!showSpinner)
    {
        printf("%s...", message.c_str());
        fflush(stdout);
        initialMessageWritten = true;
        result = action();
    }
    else
    {
        promise<void> actionIsDonePromise;
        future<void> actionIsDoneFuture(actionIsDonePromise.get_future());
        future<void> spinnerThread = async(
            launch::async,
            [&actionIsDoneFuture, initialDelayMs, &initialMessageWritten, &message]()
        {
            int retries = 0;

            // TODO (hack): use mdash instead of ndash
            array<char, 4> waiting = { '-', '\\', '|', '/' };

            bool isComplete = false;
            while (!isComplete)
            {
                if (retries == 0)
                {
                    isComplete = actionIsDoneFuture.wait_for(milliseconds(initialDelayMs)) == future_status::ready;
                }
                else
                {
                    printf("\r%s...%c", message.c_str(), waiting[(retries / 2) % waiting.size()]);
                    fflush(stdout);
                    initialMessageWritten = true;
                    isComplete = actionIsDoneFuture.wait_for(milliseconds(100)) == future_status::ready;
                }

                retries++;
            }

            if (initialMessageWritten)
            {
                // Clear out any trailing waiting character
                printf("\r%s...", message.c_str());
                fflush(stdout);
            }
        });

        result = action();
        actionIsDonePromise.set_value();
        spinnerThread.wait();
    }

    if (result)
    {
        if (initialMessageWritten)
        {
            printf("Succeeded\n");
        }
    }
    else
    {
        if (!initialMessageWritten)
        {
            printf("\r%s...", message.c_str());
            fflush(stdout);
        }

        printf("Failed.\n");
    }

    return result;
}