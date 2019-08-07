#  Kext Self-diagnostics

## Goals

In the past we've seen hangs that have been very difficult to diagnose. It would be great if there were some command(s) that we could issue to the kext to get more information about its current state (e.g. is it blocked waiting on a reply from VFS4G?).

## Technical background

Hangs in a kext usually have an adverse effect on overall system stability, particularly kexts with kauth listeners, as virtually every process in the system will trigger callbacks in the kext.
If execution doesn't progress in these callbacks, the process that caused the callback gets stuck.

The main reason for a hang in a kauth callback is that the kext has sent a message to the provider, but the provider has not replied. One possible root cause for this is simply programming error. The more worrying possibility is that the provider process has itself got stuck in a kauth kext or similar.

This has previously happened with antivirus kexts, where the AV daemon was causing a hydration message to be sent to the provider, and the provider in turn attempted to fill the file, which was immediately intercepted by the AV kext, which then waited for a scan result from the AV daemon, which in turn was waiting for the ProjFS kext's callback to progress. The AV product in question happened to have a timeout of 1 minute, so it wasn't a total deadlock, but near enough. We solved that specific issue by preventing system services from hydrating, the heuristic for "system service" being that the process and none of its ancestors are owned by a human user (UID >=500). This is not a perfect solution and indeed we have subsequently had to whitelist the `amfid`  daemon which is core to macOS to avoid problems.

Other possible reasons for a stuck kauth callback are:

 * Programming error in the kext's use of synchronisation primitives.
 * Infinite loop in an algorithm, such as when walking a directory or process hierarchy.

## Practicalities

When processes start hanging, manually initiating a diagnostic procedure is often difficult or not possible at all.
So if at all possible, it would be useful to automatically detect possible hang situations and log them locally, to be consumed later (or shortly after) by active user interaction (e.g. `gvfs diagnose`).

Unfortunately, it's essentially impossible to tell the difference between a hang or a genuinely slow operation which is making progress until the latter has resolved itself.
So only a best effort can be made: if the operation is taking more than N seconds, record it as a possible hang. If the user does not initiate a diagnostic scan, old traces can be automatically cleared eventually.
This tendency towards false positives makes hang detection a bad candidate for telemetry, but one option would be to set 2 threshold levels, one for local tracing and one that initiates telemetry feedback.
 
The `KextLogDaemon` is a good candidate for recording any hang diagnostics, as it is already running as root all the time and has an open messaging channel which could be adapted to convey more than just textual logging.
If the daemon is to write hang trace files out, the kext should completely whitelist it for all I/O without acquiring a lock, so that the very act of writing the hang log does not further tie the system up.

## Possible diagnostics

When the kext is stuck, the most important information to gather is:

 * The process and thread ID(s) that are stuck
 * The file and action whose callback the kext is stuck in
 * For how long there has been no progress. There is no hard limit for when a process is hung: hydrating a large file on a slow network connection may legitimately take a long time, while a user would probably consider hydration of a small file on a fast connection which takes more than 5 seconds to be "stuck". 
 * Where in the kext code we are stuck. Preferably including a stack trace.
 * When waiting for the provider: any metadata from the request sent to the provider.
 * Whether progress is still being made on other callbacks. (E.g. by tracking a callback counter at time intervals)

The means of obtaining this information depends on the nature of the hang:

### Waiting for a provider reply

This is a relatively infrequent operation (as a percentage of kauth callbacks), and also carries the highest risk of causing hangs as it interacts with code we cannot control or reason about reliably. It is also a comparatively expensive operation, so overhead added by extra data gathering would be minimal, making it a prime candidate for always-on, intrusive diagnostics.

All waiting callback threads already enter themselves into a global linked list. All we need to do is add some extra metadata to each list entry, and a watchdog thread can walk over them to identify items which have been waiting for an unreasonably long time. As we front-insert, the list is actually in reverse chronological order, so if we track the tail-pointer, the watchdog thread only needs to check the oldest entries.

### Other synchronisation primitives

Given that we have not had any problems in this area, it's probably not necessary to add diagnostic code, but it's useful to think about what's possible.

Fortunately, we have wrappers for all our uses of locks, so we can easily instrument all the operations.
Locks are acquired and released very, *very* frequently, so we don't want to add any overhead on the fast path.
A good option here would be to first perform a non-blocking lock using `lck_rw_try_lock()` or equivalent, and only if that fails, start recording diagnostic information and fall back to the blocking lock.
Thread safety is an issue - the very act of safely adding diagnostics could make our locking situation worse.

### Infinite loops

To protect against these, we could perhaps include counters - we should never iterate over more than `PATH_MAX` parent directories for example, and we should never iterate over more parent processes than the maximum number of processes possible in the system.

If either condition were to occur, we would need to log the incident and somehow sensibly recover, which will need to be considered on a case-by-case basis.    

There does not appear to be any evidence that this is actually occurring, and it's not clear under what circumstances it might occur, so this again may be more trouble than it's worth.

## Further mitigation strategies

 * Make waits in the kext interruptible. This at least allows the user to kill stuck processes. It potentially requires extra code at each sleep site to allow unwinding and returning `EINTR` from the system call, and needs to be done carefully, but this is definitely preferable to having to forcibly reset the machine.

## Implementation details

### Watchdog thread

When a `KextLogDaemon` connects to listen for and handle diagnostic reports, we can set up a `thread_call_t` and schedule it to run every N seconds to check for threads waiting for a provider reply.

### Stack traces

The `OSBacktrace()` KPI will dump an array of return pointers. Those alone aren't overly helpful, but if we unslide them, send them to userspace, compare them to the kext load address(es), and symbolicate each address in the binary to which we matched it, we can get a full kernel stack trace of where we've run into a problem. 
