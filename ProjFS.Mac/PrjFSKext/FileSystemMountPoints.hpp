#pragma once

struct mount;

void MountPoint_DisableAuthCache(mount* mountPoint);
void MountPoint_RestoreAuthCache(mount* mountPoint);
