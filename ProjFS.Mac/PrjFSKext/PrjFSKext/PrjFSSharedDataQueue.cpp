#include "PrjFSSharedDataQueue.hpp"
#include <IOKit/IODataQueueShared.h>
#include <libkern/OSAtomic.h>

OSDefineMetaClassAndStructors(PrjFSSharedDataQueue, IOSharedDataQueue);

// The following implementation of PrjFSSharedDataQueue::enqueue was taken from
// IOSharedDataQueue.cpp of the xnu-3248.60.10 kernel source bundle for OS X 10.11.6.
// Later versions of this code suffer from a memory ordering issue, which has been
// reported to Apple as radar issue 43093190.

/*
 * Copyright (c) 1998-2000 Apple Computer, Inc. All rights reserved.
 *
 * @APPLE_OSREFERENCE_LICENSE_HEADER_START@
 *
 * This file contains Original Code and/or Modifications of Original Code
 * as defined in and that are subject to the Apple Public Source License
 * Version 2.0 (the 'License'). You may not use this file except in
 * compliance with the License. The rights granted to you under the License
 * may not be used to create, or enable the creation or redistribution of,
 * unlawful or unlicensed copies of an Apple operating system, or to
 * circumvent, violate, or enable the circumvention or violation of, any
 * terms of an Apple operating system software license agreement.
 *
 * Please obtain a copy of the License at
 * http://www.opensource.apple.com/apsl/ and read it before using this file.
 *
 * The Original Code and all software distributed under the License are
 * distributed on an 'AS IS' basis, WITHOUT WARRANTY OF ANY KIND, EITHER
 * EXPRESS OR IMPLIED, AND APPLE HEREBY DISCLAIMS ALL SUCH WARRANTIES,
 * INCLUDING WITHOUT LIMITATION, ANY WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE, QUIET ENJOYMENT OR NON-INFRINGEMENT.
 * Please see the License for the specific language governing rights and
 * limitations under the License.
 *
 * @APPLE_OSREFERENCE_LICENSE_HEADER_END@
 */

Boolean PrjFSSharedDataQueue::enqueue(void * data, UInt32 dataSize)
{
    const UInt32       head      = dataQueue->head;  // volatile
    const UInt32       tail      = dataQueue->tail;
    const UInt32       entrySize = dataSize + DATA_QUEUE_ENTRY_HEADER_SIZE;
    IODataQueueEntry * entry;
    
    // Check for overflow of entrySize
    if (dataSize > UINT32_MAX - DATA_QUEUE_ENTRY_HEADER_SIZE) {
        return false;
    }
    // Check for underflow of (getQueueSize() - tail)
    if (getQueueSize() < tail || getQueueSize() < head) {
        return false;
    }
    
    if ( tail >= head )
    {
        // Is there enough room at the end for the entry?
        if ((entrySize <= UINT32_MAX - tail) &&
            ((tail + entrySize) <= getQueueSize()) )
        {
            entry = (IODataQueueEntry *)((UInt8 *)dataQueue->queue + tail);
            
            entry->size = dataSize;
            memcpy(&entry->data, data, dataSize);
            
            // The tail can be out of bound when the size of the new entry
            // exactly matches the available space at the end of the queue.
            // The tail can range from 0 to dataQueue->queueSize inclusive.
            
            OSAddAtomic(entrySize, (SInt32 *)&dataQueue->tail);
        }
        else if ( head > entrySize )     // Is there enough room at the beginning?
        {
            // Wrap around to the beginning, but do not allow the tail to catch
            // up to the head.
            
            dataQueue->queue->size = dataSize;
            
            // We need to make sure that there is enough room to set the size before
            // doing this. The user client checks for this and will look for the size
            // at the beginning if there isn't room for it at the end.
            
            if ( ( getQueueSize() - tail ) >= DATA_QUEUE_ENTRY_HEADER_SIZE )
            {
                ((IODataQueueEntry *)((UInt8 *)dataQueue->queue + tail))->size = dataSize;
            }
            
            memcpy(&dataQueue->queue->data, data, dataSize);
            OSCompareAndSwap(dataQueue->tail, entrySize, &dataQueue->tail);
        }
        else
        {
            return false;    // queue is full
        }
    }
    else
    {
        // Do not allow the tail to catch up to the head when the queue is full.
        // That's why the comparison uses a '>' rather than '>='.
        
        if ( (head - tail) > entrySize )
        {
            entry = (IODataQueueEntry *)((UInt8 *)dataQueue->queue + tail);
            
            entry->size = dataSize;
            memcpy(&entry->data, data, dataSize);
            OSAddAtomic(entrySize, (SInt32 *)&dataQueue->tail);
        }
        else
        {
            return false;    // queue is full
        }
    }
    
    // Send notification (via mach message) that data is available.
    
    if ( ( head == tail )                                                   /* queue was empty prior to enqueue() */
        ||   ( dataQueue->head == tail ) )   /* queue was emptied during enqueue() */
    {
        sendDataAvailableNotification();
    }
    
    return true;
}
