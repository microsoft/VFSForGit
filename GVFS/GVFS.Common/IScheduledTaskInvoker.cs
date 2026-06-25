using System.Collections.Generic;

namespace GVFS.Common
{
    /// <summary>
    /// Abstracts the Windows Task Scheduler operations needed by
    /// <see cref="LogonTaskRegistration"/>. Production callers use
    /// <see cref="SchTasksScheduledTaskInvoker"/>; tests pass a mock so
    /// they can exercise <see cref="LogonTaskRegistration"/>'s logic
    /// without actually touching the Task Scheduler on the test machine.
    /// </summary>
    public interface IScheduledTaskInvoker
    {
        /// <summary>
        /// Register the task at <paramref name="taskPath"/> from the given
        /// XML, overwriting any existing task at that path. Returns
        /// <c>true</c> on success.
        /// </summary>
        bool TryRegisterFromXml(string taskPath, string xml, out string errorMessage);

        /// <summary>
        /// Read back the registered XML for the task at
        /// <paramref name="taskPath"/>. Returns <c>true</c> with the XML
        /// when the task exists; returns <c>false</c> with a populated
        /// <paramref name="errorMessage"/> when it does not.
        /// </summary>
        bool TryQueryXml(string taskPath, out string xml, out string errorMessage);

        /// <summary>
        /// Unregister the task at <paramref name="taskPath"/>. Returns
        /// <c>true</c> if the task was unregistered OR was not registered
        /// to begin with (idempotent). Returns <c>false</c> only on a hard
        /// failure (e.g., permission denied).
        /// </summary>
        bool TryUnregister(string taskPath, out string errorMessage);
    }
}
