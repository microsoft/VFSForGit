#pragma once

namespace GvFlt
{
    public interface class ITracer
    {
        // Trace an event
        //
        // Parameters:
        //     level: EventLevel of the event
        //     eventName: Name of the event
        //     metadata: Key\value pairs of event data to be recorded
        void TraceEvent(
            Microsoft::Diagnostics::Tracing::EventLevel level,
            System::String^ eventName,
            System::Collections::Generic::Dictionary<System::String^, System::Object^>^ metadata);

        // Trace an error
        //
        // Parameters:
        //     message: Error message to record
        void TraceError(System::String^ message);

        // Trace an error
        //
        // Parameters:
        //     metadata: Key\value pairs of error data to be recorded
        void TraceError(System::Collections::Generic::Dictionary<System::String^, System::Object^>^ metadata);
    };
}