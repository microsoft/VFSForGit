#pragma once

struct mount;

bool MountPoint_Init();
void MountPoint_Cleanup();

void MountPoint_DisableAuthCache(mount* mountPoint);
void MountPoint_RestoreAuthCache(mount* mountPoint);
