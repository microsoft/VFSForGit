# Design for always-hydrate feature

## Goal
The goal of this feature is to enable a developer to communicate that they care about a set of paths,
and want their IO in those paths to always be fast. E.g. they will always build a certain subfolder,
and they don't want to pay for file-by-file downloads within that folder.

Today, a user can partially communicate this desire with the `gvfs prefetch --files` or `gvfs
prefetch --folders` commands to bulk download the files they care about. And they can further specify
the `--hydrate` flag to either command to bulk hydrate those files into the working directory.

However, this is not a great experience because it's just a one-time prefetch and hydrate. What that
means is that the next time the projection changes, e.g. after a `git checkout`, anywhere from some
to all of those files can become dehydrated, and the user will again pay the cost of file-by-file
downloads while trying to build.

With this feature, we want the developer to make one gesture and communicate that a set of files
should always be kept hydrated, even after the projection changes. That being said, a secondary goal
is to not add any significant time to commands like `git checkout` that change the projection, so the
implementation should ensure that these are done as background downloads that do not block these
commands.

## UX
This is really an extension of the existing prefetch functionality. As such, it makes sense to tie
the UX to the existing `gvfs prefetch` verb.

Side note on the existing prefetch verb: it's really two different commands in one, and it's
unfortunate that they are combined. The command `gvfs prefetch --commits` is entirely different in
its goals, usage, and implementation than either of `gvfs prefetch --files` or `gvfs prefetch 
--folders` (and the latter two can be combined with each other, but not with `--commits`). Ideally we
would have had `gvfs prefetch-commits` and `gvfs prefetch-files` as two separate commands, which
would have then led to a cleaner design for this new addition. I'm open to other suggestions here
if anyone sees a better pattern for extending the current verbs.

### Proposed UX for always-hydrate commands:
Adding new entries: 

Add an `--always-hydrate` flag to the existing `--files` and `--folders` variants of `gvfs prefetch`.
This will communicate that whatever list of folders, files, and wildcards that were passed in to the
command should be saved and reapplied after every projection change

Syntax:

```
    gvfs prefetch 
      [<Repo Root Path>]
      [--files <semicolon-delimited list of file paths, prefix * wildcards allowed>] 
      [--folders <semicolon-delimited list of folder paths, no wildcards>]
      [--folders-list <path of file containing newline-delimited paths of folders, no wildcards>] 
      [--[always-]hydrate]
```

The only change from today's syntax is the optional `[always-]` on the hydrate flag. 
`--always-hydrate` implies `--hydrate`.

Passing in the `--always-hydrate` flag has all the same behaviors that `prefetch --hydrate` has today
and also adds the specified files and folders to the `always-hydrate` file, so that those files will
continue to be hydrated in the future.

Managing existing entries:

In addition to adding entries to the `always-hydrate` file, users will also want to list out the
existing entries and remove some or all of the existing entries. To that end, we can support the
following commands:

```
    gvfs prefetch
      [<Repo Root Path>]
      --list-always-hydrate
```

The `--list-always-hydrate` flag is incompatible with any of the other flags. It will read out the
`always-hydrate` file and display the entries to the user.

```
    gvfs prefetch
      [<Repo Root Path>]
      [--files <semicolon-delimited list of file paths, prefix * wildcards allowed>] 
      [--folders <semicolon-delimited list of folder paths, no wildcards>]
      --remove-always-hydrate
```

The `--remove-always-hydrate` flag is incompatible with any combination of `--[always-]hydrate` or
`--list-always-hydrate`.

Any entries that match the specified `--files` and `--folders` arguments will be removed from the
always-hydrate file. To remove all entries, the user can specify `gvfs prefetch 
--remove-always-hydrate --files *`.

## Implementation
### Storing always-hydrate entries
We will create a new file in the `.gvfs` folder called `always-hydrate`. This file is technically
human readable and editable, but like all files in the `.gvfs` folder, carries no contract around
maintaining the same format moving forward. All user interactions with this file should go through
the `gvfs prefetch` command.

Therefore the specific format of the file is not that interesting. For the sake of easy
implementation and maintenance, we will reuse our existing file formats from our family of
`FileBasedCollection` classes, either `FileBasedDictionary` or a new type that derives from
`FileBasedCollection` in the pattern of the `PlaceholderListDatabase`.

In this case, we need to store an entry per file or folder that was specified to the prefetch
command, and we will need to remember if it was a file or a folder. Current plan is to go with a
`FileBasedDictionary` unless we find the need to store more fields.

### Hydrating files after a projection change
Whenever the projection changes, the `GitIndexProjection` class currently kicks in and updates all
placeholder files on disk with their new blob ids (for any files that changed). This is all done
synchronously and blocks the git command that caused the projection change.

In addition, this code will now also schedule background downloads of all paths that match the
`always-hydrate` collection. We will just pass the list of file and folder parameters to the existing
`BlobPrefetcher` that the `PrefetchVerb` uses to download and hydrate files today. 

That class will determine which blobs need to be downloaded and which files need to be hydrated.
However, the existing functionality of that class tries to spin up several threads and do bulk
discovery, download, and hydration of files. Since we want these to be low-priority downloads (see 
next section), we will instead just take the resulting list of blobs and paths, and schedule 
individual requests for each one. (Note: some experimentation will be needed here. It may be more
efficient to batch the downloads to some extent, but most likely in far smaller batches than the 
4000 currently in use.)

### Low priority downloads
We don't want these downloads and hydrations to interfere with any active IO that the user is
performing, therefore all download requests that come from ProjFS callbacks must always take
priority. Luckily that callback code already creates a queue of download requests, and therefore it
will be relatively straightforward to extend it to support high- and low-priority requests. 

All existing calls to `FileSystemVirtualizer.TryScheduleFileOrNetworkRequest` will be replaced with
`FileSystemVirtualizer.TryScheduleHighPriorityRequest`, and the new behavior in the post-projection
change code will call `FileSystemVirtualizer.TryScheduleLowPriorityRequest`. The existing threads in
`FileSystemVirtualizer` that service the download queue will make sure to always fully drain the high
priority queue before looking in the low priority queue for work.
