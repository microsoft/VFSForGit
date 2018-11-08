Maintenance Job Design Document
===============================

The (shared) object cache is growing to be large. This includes:

* Number of loose objects.
* Number of pack-files.
* Size of pack-files.

There are some things we can do to improve this situation, with varying levels
of difficulty and benefit. Here are the four items I believe will have a strong
impact on our data size and performance:

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

In order to perform this maintenance without disrupting users, we can actually
do these interactions in two independent jobs, with their sub-actions in a
particular order:

A. Loose Object Maintenance
  1. Delete loose objects that exist in pack-files.
  2. Place loose objects into pack-files.
B. Prefetch Pack Maintenance
  1. Delete packs with no objects referenced by the multi-pack-index.
  2. Repack some prefetch packs into larger packs.

Care must be taken to ensure that Git processes that initialized their object
store before the maintenance   still succeed. Thus, we should never delete
anything that those processes could be reading from. This is why we delete loose
objects only if they are referenced by pack-files. This is why we delete
prefetch packs only if they have no objects referenced by the multi-pack-index.
Since a second run of these jobs could delete objects that were repacked in the
previous run, we need to space our job runs such that no Git processes span both
runs.

Loose Object Job
--------------

The loose object job can save users who have many loose object downloads. These
loose objects are populated by several user activities: running `git fetch` will
get loose commits and trees for the ref tips that are not covered by prefetch
packs, hydrating a file will trigger a loose blob, and many other. Older
enlistments have many loose commits and trees due to the behavior of `git fetch`
before the `--no-show-forced-updates` option.

### Delete loose objects that appear in a pack-file

By running `git prune-packed -q` with the environment variable
`GIT_OBJECT_DIRECTORY` set to the object cache, Git will delete the loose
objects that exist in a pack-file. This will use the multi-pack-index to test
containment, so is only bound by the file-deletion operations.

### Pack loose objects that do not appear in a pack-file

We can use `git pack-objects -q <object-dir>/pack/loose-<timestamp>` to create a
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

Prefetch Pack Job
---------------

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

Running the Jobs
----------------

We already have a post-fetch job that is automatically queued in the background.
We should rename this job to be a "maintenance" job that could trigger several
actions. The maintenance job should run every 15 minutes (as the post-fetch job
currently does) but then it should check each "sub-job" for a timing condition
to run again.

For example:

* The post-fetch job doesn't trigger if the most-recent prefetch pack timestamp
  is within the last 70 minutes.
* The loose object job doesn't trigger if the most-recent "loose" pack timestamp
  is within the last 24 hours.
* The prefetch repack job doesn't trigger if the most-recent "repacked" pack
  timestamp is within the last 7 days or the most-recent "repacked" pack is
  modified within the last 24 hours.
