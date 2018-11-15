# Vnode Cache Design

## Goals

- Reduce the time required to determine if a file is inside of a virtualization root
- Reduce the time required to find a file's virtualization root
- **TBD:** Eliminate the use of file flags to determine if a file is inside of a root

## Design 

### Cached Data

PrjFSKext will cache the following information for each vnode:

- The `vnode_t` itself
- The vnode's `uint32_t` vid (generation number)
- `VirtualizationRootHandle` for the vnode (or `RootHandle_None` if the vnode is not in a root).  **NOTE** This assumes that `VirtualizationRootHandle`  are not recycled as VFS4G instances are unmounted\remounted.

The data for each vnode will be stored in a hash table that assumes there will be `desiredvnodes` total vnodes.

### Using the Cache

When PrjFSKext needs to determine the virtualization root of a file (or if a file is inside of a virtualization root at all), it will look up the vnode in its cache.

- If the vnode exists in the cache and its `vid` has not changed, PrjFSKext will use the `VirtualizationRootHandle` in the cache.

- If the vnode exists in the cache and its `vid` has changed, PrjFSKext will lookup its `VirtualizationRootHandle` (using the existing code in PrjFSKext) and update the cache entry.

- If the vnode does not exist in the cache,  PrjFSKext will lookup its `VirtualizationRootHandle` (using the existing code in PrjFSKext) and insert the vnode into the cache.

### Special Case: Renames

*Directory renames* 

Directory renames are a special case because all of the directory's children may need to have their `VirtualizationRootHandle` updated. Rather than recording\tracking all of the parent\child relationships among the vnodes PrjFSKext will simply invalidate the entire cache when it detects that a directory has been renamed in\out of a virtualization root.  If PrjFSKext is unable to determine if the rename was within the same root (e.g. the directory's vnode was recycled) then PrjFSKext will invalidate the cache.

*File renames*

When a file is renamed PrjFSKext will update its `VirtualizationRootHandle` and\or `vid` as needed.

## Future Enhancements

- The vnode cache could track which vnodes have received the `KAUTH_FILEOP_WILL_RENAME` file operation (on Mojave) so that when handling `KAUTH_VNODE_DELETE` the kext can know if the delete vnode operation is for a rename.

## Issues not addressed by the cache

- Improving the speed and reliability of finding a vnode's path
- Addressing failures to get a vnode's parent (when the vnode is not in the cache)
- Finding the roots for hard links that span repos (and general handling of hard links that cross repo boundaries)

## Open Questions

- During a vnode operation, when a vnode does not exist in the cache (or has been recycled since being added to the cache) should PrjFSKext check `FileFlags_IsInVirtualizationRoot` before it walks up the tree looking for a root?
- Will PrjFSKext invalidate the cache too frequently if it does so on every directory rename?
- What is the timeline for allowing breaking changes to existing repos (as we refine the approach that we're taking)?
