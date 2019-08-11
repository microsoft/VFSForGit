#include "Locks.hpp"
#include "public/Message.h"
#include <sys/kernel_types.h>
#include <sys/syslimits.h>

class KextLogTracer
{
    bool discarded;
    bool willEmitTrace;
    bool hasTraceIndex;
    char embeddedTraceBuffer[512];
    char* dynamicTraceBuffer;
    uint32_t dynamicTraceBufferSize;
    uint32_t traceBufferPosition;
    uint64_t traceIndex;

public:
    static RWLock traceFilterLock;
    static _Atomic(char*) pathPrefixFilter;
    static _Atomic(kauth_action_t) traceVnodeActionFilterMask;
    static _Atomic(bool) traceDeniedVnodeEvents;
    static _Atomic(bool) traceProviderMessagingEvents;
    static _Atomic(bool) traceAllFileOpEvents;
    static _Atomic(bool) traceAllVnodeEvents;
    static _Atomic(bool) traceCrawlerEvents;
    static _Atomic(uint64_t) nextTraceIndex;
private:

    KextLogTracer() = delete;
    KextLogTracer(const KextLogTracer&) = delete;
    KextLogTracer& operator=(const KextLogTracer&) = delete;

    struct TraceBuffer
    {
        char* buffer;
        uint32_t size, position;
    };

    static bool ShouldTraceEventsForVnode(vnode_t vnode, char (&pathBuffer)[PATH_MAX]);
    uint64_t GetTraceIndex();
    TraceBuffer GetBuffer();
    bool GrowBuffer(uint32_t minimumSize);
    void Emit();
    void Flush();
    
public:
    explicit KextLogTracer(kauth_action_t vnodeAction, vnode_t vnode);
    void SendProviderMessage(MessageType providerMessage);
    void SendProviderMessageResult(bool success);
    void SetVnodeOpResult(int result);
    ~KextLogTracer();

    template <typename... ARGS>
        void Printf(const char* format, ARGS... args);

    static void Initialize();
    static void Cleanup();
};
