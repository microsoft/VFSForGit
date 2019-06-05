#include "kext-perf-tracing.hpp"
#include "../../PrjFSKext/public/ArrayUtils.hpp"
#include "../../PrjFSKext/public/PrjFSCommon.h"
#include "../../PrjFSKext/public/PrjFSPerfCounter.h"
#include "../../PrjFSKext/public/PrjFSLogClientShared.h"
#include <mach/mach_time.h>
#include <dispatch/dispatch.h>
#include <IOKit/IOKitLib.h>
#include <cstdio>
#include <cmath>
#include <algorithm>
#include <string>
#include <iterator>

// non-breaking space
#define NBSP_STR u8"\u00A0"

using std::max;
using std::string;
using std::begin;
using std::end;

static mach_timebase_info_data_t s_machTimebase;

static uint64_t nanosecondsFromAbsoluteTime(uint64_t machAbsoluteTime)
{
    return static_cast<__uint128_t>(machAbsoluteTime) * s_machTimebase.numer / s_machTimebase.denom;
}

static constexpr const char* const PerfCounterNames[PrjFSPerfCounter_Count] =
{
    [PrjFSPerfCounter_VnodeOp]                                              = "HandleVnodeOperation",
    [PrjFSPerfCounter_VnodeOp_GetPath]                                      = " |--GetPath",
    [PrjFSPerfCounter_VnodeOp_BasicVnodeChecks]                             = " |--BasicVnodeChecks",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle]                                 = " |--ShouldHandleVnodeOpEvent",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_IsVnodeAccessCheck]              = " |  |--IsVnodeAccessCheck",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_IgnoredVnodeAccessCheck]         = " |  |  |--IgnoredVnodeAccessCheck",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_ReadFileFlags]                   = " |  |--TryReadVNodeFileFlags",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_NotInAnyRoot]                    = " |  |  |--NotInAnyRoot",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_CheckFileSystemCrawler]          = " |  |--IsFileSystemCrawler",
    [PrjFSPerfCounter_VnodeOp_ShouldHandle_DeniedFileSystemCrawler]         = " |     |--Denied",
    [PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot]                        = " |--TryGetVirtualizationRoot",
    [PrjFSPerfCounter_VnodeOp_Vnode_Cache_Hit]                              = " |  |--VnodeCacheHit",
    [PrjFSPerfCounter_VnodeOp_Vnode_Cache_Miss]                             = " |  |--VnodeCacheMiss",
    [PrjFSPerfCounter_VnodeOp_FindRoot]                                     = " |  |  |--FindForVnode",
    [PrjFSPerfCounter_VnodeOp_FindRoot_Iteration]                           = " |  |  |  |--inner_loop_iterations",
    [PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_TemporaryDirectory]     = " |  |--TemporaryDirectory",
    [PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_NoRootFound]            = " |  |--NoRootFound",
    [PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_ProviderOffline]        = " |  |--ProviderOffline",
    [PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_OriginatedByProvider]   = " |  |--OriginatedByProvider",
    [PrjFSPerfCounter_VnodeOp_GetVirtualizationRoot_UserRestriction]        = " |  |--UserRestriction",
    [PrjFSPerfCounter_VnodeOp_PreDelete]                                    = " |--RaisePreDeleteEvent",
    [PrjFSPerfCounter_VnodeOp_EnumerateDirectory]                           = " |--RaiseEnumerateDirectoryEvent",
    [PrjFSPerfCounter_VnodeOp_RecursivelyEnumerateDirectory]                = " |--RaiseRecursivelyEnumerateEvent",
    [PrjFSPerfCounter_VnodeOp_HydrateFile]                                  = " |--RaiseHydrateFileEvent",
    [PrjFSPerfCounter_VnodeOp_PreConvertToFull]                             = " |--RaisePreConvertToFull",
    [PrjFSPerfCounter_FileOp]                                               = "HandleFileOpOperation",
    [PrjFSPerfCounter_FileOp_ShouldHandle]                                  = " |--ShouldHandleFileOpEvent",
    [PrjFSPerfCounter_FileOp_ShouldHandle_FindVirtualizationRoot]           = " |  |--FindVirtualizationRoot",
    [PrjFSPerfCounter_FileOp_Vnode_Cache_Hit]                               = " |  |  |--VnodeCacheHit",
    [PrjFSPerfCounter_FileOp_Vnode_Cache_Miss]                              = " |  |  |--VnodeCacheMiss",
    [PrjFSPerfCounter_FileOp_FindRoot]                                      = " |  |  |  |--FindForVnode",
    [PrjFSPerfCounter_FileOp_FindRoot_Iteration]                            = " |  |  |  |  |--inner_loop_iterations",
    [PrjFSPerfCounter_FileOp_ShouldHandle_NoRootFound]                      = " |  |  |--NoRootFound",
    [PrjFSPerfCounter_FileOp_ShouldHandle_FindProviderPathBased]            = " |  |--FindActiveProviderForPath",
    [PrjFSPerfCounter_FileOp_ShouldHandle_NoProviderFound]                  = " |  |  |--NoProviderFound",
    [PrjFSPerfCounter_FileOp_ShouldHandle_CheckProvider]                    = " |  |--CheckProvider",
    [PrjFSPerfCounter_FileOp_ShouldHandle_OfflineRoot]                      = " |  |  |--OfflineRoot",
    [PrjFSPerfCounter_FileOp_ShouldHandle_OriginatedByProvider]             = " |     |--OriginatedByProvider",
    [PrjFSPerfCounter_FileOp_Renamed]                                       = " |--RaiseRenamedEvent",
    [PrjFSPerfCounter_FileOp_HardLinkCreated]                               = " |--RaiseHardLinkCreatedEvent",
    [PrjFSPerfCounter_FileOp_FileModified]                                  = " |--RaiseFileModifiedEvent",
    [PrjFSPerfCounter_FileOp_FileCreated]                                   = " |--RaiseFileCreatedEvent",
    [PrjFSPerfCounter_CacheCapacity]                                        = "VnodeCacheCapacity",
    [PrjFSPerfCounter_CacheInvalidateCount]                                 = "VnodeCacheInvalidationCount",
    [PrjFSPerfCounter_CacheFullCount]                                       = "VnodeCacheFullCount",
};

static_assert(AllArrayElementsInitialized(PerfCounterNames), "There must be an initialization of PerfCounterNames elements corresponding to each PrjFSPerfCounter enum value");


static double FindSuitablPrefixedUnitFromNS(double nanoSeconds, const char*& outUnit)
{
    double value = nanoSeconds;
    outUnit = "ns";
    if (value > 1000.0)
    {
        value /= 1000.0;
        outUnit = "µs";
        if (value > 1000.0)
        {
            value /= 1000.0;
            outUnit = "ms";
            if (value > 1000.0)
            {
                value /= 1000.0;
                outUnit = "s" NBSP_STR;
            }
        }
    }
    
    return value;
}

static void PadStringRight(string& s, const string& with, size_t padWidth)
{
    s.reserve(s.size() + with.size() * padWidth);
    for (size_t i = 0; i < padWidth; ++i)
    {
        s += with;
    }
}

static string GenerateHistogramScaleLabel()
{
    string histogramScaleLabel;
    // Generate 5 scale labels, aiming for an interval of 10 buckets (Factor of 2¹⁰ = 1024 between labels.)
    // This should produce something like "|←1.00ns  |←1.02µs  |←1.05ms  |←1.07s   |←1100s  "
    // Variation between Mach Absolute Time units on different hardware will determine the exact labels.
    // Most of this code is tricky for 2 reasons: left-alignment of labels (printf formatting right-aligns)
    // and use of non-ASCII unicode code points which throw off the byte & character column correspondence.
    // This means we can't use the byte position for histogram bucket alignment.
    unsigned int markerBucketPosition = 0;
    for (unsigned int marker = 0; marker < 5; ++marker)
    {
        double markerNanoseconds =
            static_cast<double>(UINT64_C(1) << markerBucketPosition) * s_machTimebase.numer / s_machTimebase.denom;
        const char* prefixedUnit;
        double markerTime = FindSuitablPrefixedUnitFromNS(markerNanoseconds, prefixedUnit);
        
        char markerLabel[20];
        int scaleLabelWidth = snprintf(
            markerLabel,
            sizeof(markerLabel),
            "%.*f",
            markerTime > 999 ? 0 : 2, // Drop decimals for labels with 4+ integral digits
            markerTime);
        
        histogramScaleLabel += "|←";
        histogramScaleLabel += markerLabel;
        histogramScaleLabel += prefixedUnit;
        
        // This is counting character columns, not bytes
        scaleLabelWidth += 2 + 2; // 2 for "|←" and 2 for "ns"/"µs"/"s "/etc.

        // Aim for 10 columns, but if it's more, ensure everything else is shifted along
        int rightPadWidth = max(0, 10 - scaleLabelWidth);
        PadStringRight(histogramScaleLabel, NBSP_STR, rightPadWidth);
        markerBucketPosition += scaleLabelWidth + rightPadWidth;
    }
    
    // The text on the right hand side of the histogram's column header
    static const char histogramHeaderTitle[] = "Histogram" NBSP_STR;
    static const size_t histogramHeaderTitleColumns = 10; // can't use strlen due to UTF-8
    // Padding to fill out the character columns between the marker labels and the header title
    PadStringRight(histogramScaleLabel, NBSP_STR, PrjFSPerfCounterBuckets - histogramHeaderTitleColumns - markerBucketPosition);
    histogramScaleLabel += histogramHeaderTitle;
    return histogramScaleLabel;
}

bool PrjFSLog_FetchAndPrintKextProfilingData(io_connect_t connection)
{
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        mach_timebase_info(&s_machTimebase);
    });
    
    PrjFSPerfCounterResult counters[PrjFSPerfCounter_Count];
    size_t out_size = sizeof(counters);
    IOReturn ret = IOConnectCallStructMethod(connection, LogSelector_FetchProfilingData, nullptr, 0, counters, &out_size);
    if (ret == kIOReturnUnsupported)
    {
        return false;
    }
    else if (ret == kIOReturnSuccess)
    {
        static std::string histogramScaleLabel = GenerateHistogramScaleLabel();
        
        printf("   Counter                             [ Samples  ][Total time (ns)][Mean (ns)   ][Min (ns)][Max (ns)  ][%s]\n",
            histogramScaleLabel.c_str());
        printf("--------------------------------------------------------------------------------------------------------------------------------------------------------------------------\n");
        
        for (int32_t i = 0; i < PrjFSPerfCounter_Count; ++i)
        {
            double numSamples = counters[i].numSamples;
            printf(
                "%2u %-35s [%10llu]",
                i,
                PerfCounterNames[i],
                counters[i].numSamples);
            
            if (counters[i].min != UINT64_MAX)
            {
                // The values on the counter are reported in units of mach absolute time
                double sum = counters[i].sum;

                uint64_t sumNS = nanosecondsFromAbsoluteTime(sum);
                uint64_t meanNS = numSamples > 0 ? sumNS / numSamples : 0;

                printf(
                    "[%15llu][%10llu][%10llu][%10llu]",
                    sumNS,
                    meanNS,
                    nanosecondsFromAbsoluteTime(counters[i].min),
                    nanosecondsFromAbsoluteTime(counters[i].max));
                
                static const char* const barGraphItems[9] = {
                    NBSP_STR, "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█",
                };
                
                // Find the bucket with the largest number of samples; use the 8/8
                // bar symbol for that, and make all other buckets relative to it.
                // (Defining the overall number of samples across all buckets as
                // 100% on the scale would limit the resolution of the information
                // you could read from the graph; the 8/8 bar symbol would only be
                // used on distributions very concentrated on one bucket.)
                _Atomic uint64_t(&buckets)[PrjFSPerfCounterBuckets] = counters[i].sampleBuckets;
                uint64_t bucketMax = *std::max_element(begin(buckets), end(buckets));
                if (bucketMax > 0) // Should normally not be 0 if we get here, but defends against divide by 0 in case of a bug
                {
                    printf("[");
                    for (size_t bucket = 0; bucket < PrjFSPerfCounterBuckets; ++bucket)
                    {
                        // Always round up so we have a clear distinction between buckets with zero and even a single item.
                        uint64_t eighths = (8 * buckets[bucket] + bucketMax - 1) / bucketMax;
                        assert(eighths >= 0);
                        assert(eighths <= 8);
                        printf("%s", barGraphItems[eighths]);
                    }
                    printf("]");
                }
            }
            printf("\n");
        }
    }
    else
    {
        fprintf(stderr, "fetching profiling data from kernel failed: 0x%x\n", ret);
        return false;
    }
    
    printf("\n");
    fflush(stdout);
    
    return true;
}
