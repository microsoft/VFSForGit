Maintenance Job Design Document
===============================

The shared cache is growing to be large. This includes:

* Object Cache:
  * Number of loose objects.
  * Number of pack-files.
  * Size of pack-files.
* Object Size Database

This document proposes a background maintenance task to help reduce the
size of this cache and improve Git performance as a side-effect. This
proposal focuses on concrete actions on the object cache, but the concepts
of scheduling background tasks can extend to the object size database.

Object Cache Maintenance
------------------------

There are some things we can do to clean up the object cache, with varying
levels of difficulty and benefit. Here are the four items I believe will
have a strong impact on our data size and performance:

I. Delete loose objects that exist in pack-files.
II. Place loose objects into pack-files.
III. Repack some prefetch packs into larger packs.
IV. Delete packs with no objects referenced by the multi-pack-index.

These are numbered by order of implementation, but they work together in an
interesting way. If we place loose objects into pack-files (I), then the next
time we delete loose objects (II) we will delete those objects that we just put
into a pack-file. If we repack some prefetch packs into larger packs (III), the
multi-pack-index will use the "newer" pack for all objects in those packs, and
we can delete them (IV).

In order to perform this maintenance without disrupting users, we must be
careful in what order we perform these actions. The order must be as given
below:

A. Loose Object Cleanup Action
  1. Delete loose objects that exist in pack-files.
  2. Place loose objects into pack-files.
B. Prefetch Pack Cleanup Action
  1. Delete packs with no objects referenced by the multi-pack-index.
  2. Repack some prefetch packs into larger packs.

Care must be taken to ensure that Git processes that initialized their object
store before the maintenance still succeed. Thus, we should never delete
anything that those processes could be reading from. This is why we delete loose
objects only if they are referenced by pack-files. This is why we delete
prefetch packs only if they have no objects referenced by the multi-pack-index.
Since a second run of these jobs could delete objects that were repacked in the
previous run, we need to space our job runs such that no Git processes span both
runs.

The loose object maintenance can happen at a different cadence than the prefetch
pack maintenance, or they can happen at the same time. See the discussion on
"scheduling actions" below.

Loose Object Cleanup Action
---------------------------

The loose object action can save users who have many loose object downloads.
These loose objects are populated by several user activities: running
`git fetch` will get loose commits and trees for the ref tips that are not
covered by prefetch packs, hydrating a file will trigger a loose blob, and many
other. Older enlistments have many loose commits and trees due to the behavior
of `git fetch` before the `--no-show-forced-updates` option.

### Delete loose objects that appear in a pack-file

By running `git prune-packed -q` with the environment variable
`GIT_OBJECT_DIRECTORY` set to the object cache, Git will delete the loose
objects that exist in a pack-file. This will use the multi-pack-index to test
containment, so is only bound by the file-deletion operations.

### Pack loose objects that do not appear in a pack-file

We can use `git pack-objects -q <object-dir>/pack/fromloose-<timestamp>` to create a
pack-file filled with a list of loose objects. We can pass the list of loose
objects to this command through stdin. We can choose to batch this operation by
limiting the number of objects we pack at a time, if we want. We may also want
to skip this step if the number of loose objects is low (&lt; 1,000? &lt;
10,000?)

By listing a timestamp in the pack-file name, we can store the "most-recent" 
time this job was run.

*Note:* While we are thinking about packing loose objects, we can consider
_deleting_ some loose blobs. It is easier to delete loose blobs than packed
blobs. If we want to delete "stale" blobs, then we should add a step to this
job.

Prefetch Pack Cleanup Action
----------------------------

We store a list of prefetch packs as `<object-dir>/pack/prefetch-<timestamp>-<sha>.[pack|idx]`.
These are downloaded at a regular cadence by the post-fetch job, and could have
a new pack-file for every hour. In addition to these prefetch packs, some daily
prefetch packs will appear and occasionally a very large prefetch pack covering
everything more than 30 days old. This large prefetch pack appears on initial
clone and sometimes again if a user does not prefetch for 30 days (this occurs 
less often now that background prefetch exists).

### Delete pack-files with no objects referenced by the multi-pack-index

The multi-pack-index stores a single copy of every packed object. When an object
appears in multiple pack-files covered by the multi-pack-index, we select the
pack-file with the most-recent _modified time_ as reported by the filesystem.
For example, if someone downloads a second large prefetch pack when prefetching
for the first time in 31 days, then that large prefetch pack is referenced and
all older prefetch packs are "stale" and not referenced by the multi-pack-index.

We can extend the `git multi-pack-index` builtin to delete pack-files that have
no objects referenced by the multi-pack-index. This should happen at the same
time as we update the multi-pack-index since we don't want to have this list of
pack-files in the multi-pack-index if they don't exist. We also don't want to
remove them from the multi-pack-index while the pack-files still exist, because
then a Git process will start referencing those pack-files in the `packed_git`
linked list.

### Repack the prefetch pack-files

We can reduce the number of prefetch packs and improve file locality by
combining prefetch packs into new "repacked" pack-files. Use `git show-index
<object-dir>/pack/prefetch-<timestamp[i]>-<hash[i]>.idx` to list the objects
in the prefetch packs and pipe that output to `git pack-objects -q
<object-dir>/pack/repacked-<timestamp>`.

We should refrain from packing the most-recent prefetch packs, as we don't want
to get in the way of the prefetch machinery for determining the most-recent
timestamp. We could, however, start repacking the prefetch packs from
oldest-to-newest, stopping in the most-recent week, or when the prefetch packs
are large enough to satisfy a batch. Some stopping conditions could include
"pack-file size is above 1GB".

Scheduling Actions
------------------

We already have a prefetch action that is automatically queued in the
background. In addition, a `gvfs prefetch --commits` action sends a message
to the mount process to run a post-fetch job (compute multi-pack-index and
commit-graph). These actions already have some logic that we will want to
replicate, so we should make them explicit requirements of an action:

* Most actions intend to have some delay between successful runs. Use a
  `IsReady(DateTime time)` method to perform only the check if the given
  `time` is long enough after the previous run. (This is most likely stored
  as a timestamp in a list of pack-files or as the content of a file in
  the shared object directory.)
* Most logic happens during an `Execute()` method.
* If the mount process needs to shut down while the action is active, we need
  a `Stop()` method for the action to interrupt itself. This includes halting
  any Git processes that are being run.

The actions are then run in sequence by an `ActionRunner` that triggers a run
even on some interval, runs the actions that are ready, and tracks the
currently-running action (so the mount process can tell the `ActionRunner` to
`Stop()` and it will stop the running action). The `ActionRunner` can keep an
ordered list of actions, and during each run of the background task it can
check `IsReady(DateTime.UtcNow)` and run `Execute()` as necessary.

Here are some initial ideas for the frequency of each action:

* The prefetch action doesn't trigger if the most-recent prefetch pack timestamp
  is within the last 70 minutes.
* The loose object action doesn't trigger if the most-recent "fromloose" pack
  timestamp is within the last 24 hours.
* The prefetch repack action doesn't trigger if the most-recent "repacked" pack
  timestamp is within the last 7 days or the most-recent "repacked" pack is
  modified within the last 24 hours.

In addition to the background maintenance, we can run the actions directly
by creating a new `MaintenanceVerb` so commands like `gvfs maintenance [--loose|--packs]` will run the maintenance directly.

### Watching Git Process IDs

A possible direction for ensuring we don't delete a file that could be
referenced by a Git process is to record a list of Git process IDs that are
running as an action completes. We can then verify that those processes are
not running the next time we run the action. See [`IsBlockingProcessRunning()`](https://github.com/Microsoft/VFSForGit/blob/4057d91a585b4f188874f8636b4390da402a367c/GVFS/GVFS.Common/InstallerPreRunChecker.cs#L129-L147)
for an example of how to check the process list.

We could centralize this Git process logic into an `ActionHelper` class that
manages storing a "last-run information" file for each action, and that can
include the previous-run timestamp and a list of Git processes. Storing the
data in JSON could make this extensible in the future.

Testing Strategy
----------------

We can test each action individually using functional tests.

The `IsReady(time)` methods are simple to test, and allows us to check cases
such as:

* No previous time listed on the filesystem.
* Time is old enough to return `true`.
* Time is new enough to return `false`, both before and after `time`.

The `Execute()` method requires a bit more work. We should focus on pre- and
post-conditions to verify the actions do what we think they are doing, and
satisfy the "contract" with Git that we don't delete files that Git may be
reading at the time. We can also create tests that run some Git commands in
parallel, but we can't control the timing of when the Git commands load the
pack-files versus loose objects. The best option (that I can think of) is to
run `git rev-list --objects --all` on loop until the `Execute()` method returns.
This may catch unexpected behavior, but is likely to not fail consistently.
Instead, the pre- and post-condition tests should be used to verify correctness
more consistently.

The `Stop()` method is important to test in the following states:

* `Execute()` has not run.
* `Execute()` has completed.
* `Execute()` is running. This may require injecting "pauses" into the action
  to check different stages during the execution. We could insert callbacks in
  the action's constructor (that are `null` outside of tests) that allow us to
  call `Stop()` at specific times in the `Execute()` method.

How does the post-fetch job fit in?
-----------------------------------

While working on a basic refactoring to prepare for this work, I stumbled with
fitting the existing background prefetch logic and the post-fetch job into
a cohesive structure. The tricky part is that the post-fetch job doesn't run
on a schedule, but instead after every prefetch. Part of the logic requires
a list of pack-files that were just added to the object cache. The job is
triggered by the background prefetch _and_ a `gvfs prefetch --commits` command.

To combine this into the `ActionRunner` structure, I propose the following:

1. The post-fetch job becomes the "cache update action" (it updates the
   multi-pack-index and commit-graph files in the cache), and stores an
   (initially `null`) list of strings for the prefetch pack-files to use in the `git commit-graph write`
   command.
2. `IsReady(time)` returns `true` when the pack-file list is non-null.
3. When the mount process gets a post-fetch job request, it sends the pack-files
   to the `ActionRunner`, which sends them to the cache update action.
   The `ActionRunner` then triggers a maintenance task ahead of schedule.

There is an unresolved question in the design above: what happens when the
maintenance task is already running? In my current thought, we should expect
this to be the case, because the background prefetch is part of that task, and
it sends the message to the in-process mount, so is still running when the
task is happening. One way to resolve this is to have the cache update action
be the very last action run during the maintenance task. **Thoughts?**
