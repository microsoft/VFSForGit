project('GVFS.ReadObjectHook.Linux', 'cpp',
  default_options : ['c_std=c11', 'cpp_std=c++14'],
  license : 'MIT')

src = ['main.cpp', 'packet.cpp', 'stdafx.cpp',
       '../GVFS.NativeHooks.Common/common.posix.cpp']

incdir = include_directories('../GVFS.NativeHooks.Common')

executable('GVFS.ReadObjectHook', sources : src, include_directories : incdir)
