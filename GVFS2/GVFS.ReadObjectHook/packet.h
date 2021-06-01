#pragma once
#include <stdio.h>

size_t packet_txt_read(char *buf, size_t count, FILE *stream = stdin);
void packet_txt_write(const char *buf, FILE *stream = stdout);
void packet_flush(FILE *stream = stdout);
