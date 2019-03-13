project(
  'GVFS.PostIndexChangedHook', 'cpp',
  default_options : [
    'c_std=c11',
    'cpp_std=c++14',
    'warning_level=3',
    'werror=true'
  ],
  license : 'MIT'
)

add_project_link_arguments('-static-libstdc++', language : 'cpp')

sources = files([
  '../GVFS.NativeHooks.Common/common.posix.cpp',
  'main.cpp',
  'stdafx.cpp'
])

incdir = include_directories('../GVFS.NativeHooks.Common')

executable(
  'GVFS.PostIndexChangedHook', sources,
  include_directories : incdir
)
