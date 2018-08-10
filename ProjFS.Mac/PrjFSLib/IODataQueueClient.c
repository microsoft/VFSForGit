// This code imported from IOKitUser-1445.40.1 source bundle at
// https://opensource.apple.com/source/IOKitUser/IOKitUser-1445.40.1/

/*
 * Copyright (c) 1998-2000 Apple Computer, Inc. All rights reserved.
 *
 * @APPLE_LICENSE_HEADER_START@
 *
 * This file contains Original Code and/or Modifications of Original Code
 * as defined in and that are subject to the Apple Public Source License
 * Version 2.0 (the 'License'). You may not use this file except in
 * compliance with the License. Please obtain a copy of the License at
 * http://www.opensource.apple.com/apsl/ and read it before using this
 * file.
 *
 * The Original Code and all software distributed under the License are
 * distributed on an 'AS IS' basis, WITHOUT WARRANTY OF ANY KIND, EITHER
 * EXPRESS OR IMPLIED, AND APPLE HEREBY DISCLAIMS ALL SUCH WARRANTIES,
 * INCLUDING WITHOUT LIMITATION, ANY WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE, QUIET ENJOYMENT OR NON-INFRINGEMENT.
 * Please see the License for the specific language governing rights and
 * limitations under the License.
 *
 * @APPLE_LICENSE_HEADER_END@
 */

#include <IOKit/IODataQueueShared.h>
#pragma clang diagnostic ignored "-Wdeprecated-declarations"

IOReturn
IODataQueueDequeue(IODataQueueMemory *dataQueue, void *data, uint32_t *dataSize)
{
    IOReturn            retVal          = kIOReturnSuccess;
    IODataQueueEntry *  entry           = 0;
    UInt32              entrySize       = 0;
    UInt32              newHeadOffset   = 0;

    if (dataQueue) {
        if (dataQueue->head != dataQueue->tail) {
            IODataQueueEntry *  head        = 0;
            UInt32              headSize    = 0;
            UInt32              headOffset  = dataQueue->head;
            UInt32              queueSize   = dataQueue->queueSize;
            
            head         = (IODataQueueEntry *)((char *)dataQueue->queue + headOffset);
            headSize     = head->size;
            
            // we wraped around to beginning, so read from there
            // either there was not even room for the header
            if ((headOffset + DATA_QUEUE_ENTRY_HEADER_SIZE > queueSize) ||
                // or there was room for the header, but not for the data
                ((headOffset + headSize + DATA_QUEUE_ENTRY_HEADER_SIZE) > queueSize)) {
                entry       = dataQueue->queue;
                entrySize   = entry->size;
                newHeadOffset = entrySize + DATA_QUEUE_ENTRY_HEADER_SIZE;
            // else it is at the end
            } else {
                entry = head;
                entrySize = entry->size;
                newHeadOffset = headOffset + entrySize + DATA_QUEUE_ENTRY_HEADER_SIZE;
            }
        }

        if (entry) {
            if (data) {
                if (dataSize) {
                    if (entrySize <= *dataSize) {
                        memcpy(data, &(entry->data), entrySize);
                        OSAtomicCompareAndSwap32Barrier(dataQueue->head, newHeadOffset, (int32_t *)&dataQueue->head);
                    } else {
                        retVal = kIOReturnNoSpace;
                    }
                } else {
                    retVal = kIOReturnBadArgument;
                }
            } else {
                OSAtomicCompareAndSwap32Barrier(dataQueue->head, newHeadOffset, (int32_t *)&dataQueue->head);
            }

            // RY: Update the data size here.  This will
            // ensure that dataSize is always updated.
            if (dataSize) {
                *dataSize = entrySize;
            }
        } else {
            retVal = kIOReturnUnderrun;
        }
    } else {
        retVal = kIOReturnBadArgument;
    }
    
    return retVal;
}
