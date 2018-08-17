#!/bin/bash

TESTDIR=~/CachePoisonTest/$1

echo Enumerating A asynchronously. The provider sleeps in response, so this hangs.
ls $TESTDIR/Target/src/A &

echo Sleeping 1 second...
sleep 1.0s 

echo Enumerating A/B/2.txt. This SHOULD hang, but without a fix in the kext, we instead see an error that the file does not exist. 
ls $TESTDIR/Target/src/A/B/2.txt 

echo Sleeping 3 seconds... 
sleep 3.0s

echo Enumerating A/B/2.txt again. Now that we have waited for the kauth TTL to expire, this hangs even without a fix.
ls $TESTDIR/Target/src/A/B/2.txt 
