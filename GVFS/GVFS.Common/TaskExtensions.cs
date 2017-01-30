using System;
using System.Threading.Tasks;

namespace GVFS.Common
{
    public static class TaskExtensions
    {
        public static void Timeout<TTimeoutException>(this Task self, int timeoutMs)
            where TTimeoutException : TimeoutException, new()
        {
            if (!self.Wait(timeoutMs))
            {
                throw new TTimeoutException();
            }
        }

        public static T Timeout<T, TTimeoutException>(this Task<T> self, int timeoutMs)
            where TTimeoutException : TimeoutException, new()
        {
            if (!self.Wait(timeoutMs))
            {
                throw new TTimeoutException();
            }

            return self.Result;
        }

        public static async Task TimeoutAsync<TTimeoutException>(this Task self, int timeoutMs)
            where TTimeoutException : TimeoutException, new()
        {
            Task timeout = Task.Delay(timeoutMs);
            Task completedFirst = await Task.WhenAny(timeout, self);
            if (timeout == completedFirst)
            {
                throw new TTimeoutException();
            }
        }

        public static async Task<T> TimeoutAsync<T, TTimeoutException>(this Task<T> self, int timeoutMs)
            where TTimeoutException : TimeoutException, new()
        {
            Task timeout = Task.Delay(timeoutMs);
            Task completedFirst = await Task.WhenAny(timeout, self);
            if (timeout == completedFirst)
            {
                throw new TTimeoutException();
            }

            return await self;
        }
    }
}
