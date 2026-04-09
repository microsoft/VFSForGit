#include "stdafx.h"
#include "packet.h"
#include "common.h"

static void set_packet_header(char *buf, const size_t size)
{
	static char hexchar[] = "0123456789abcdef";

#define hex(a) (hexchar[(a) & 15])
	buf[0] = hex(size >> 12);
	buf[1] = hex(size >> 8);
	buf[2] = hex(size >> 4);
	buf[3] = hex(size);
#undef hex
}

const signed char hexval_table[256] = {
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 00-07 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 08-0f */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 10-17 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 18-1f */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 20-27 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 28-2f */
	0,  1,  2,  3,  4,  5,  6,  7,		/* 30-37 */
	8,  9, -1, -1, -1, -1, -1, -1,		/* 38-3f */
	-1, 10, 11, 12, 13, 14, 15, -1,		/* 40-47 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 48-4f */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 50-57 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 58-5f */
	-1, 10, 11, 12, 13, 14, 15, -1,		/* 60-67 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 68-67 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 70-77 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 78-7f */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 80-87 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 88-8f */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 90-97 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* 98-9f */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* a0-a7 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* a8-af */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* b0-b7 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* b8-bf */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* c0-c7 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* c8-cf */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* d0-d7 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* d8-df */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* e0-e7 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* e8-ef */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* f0-f7 */
	-1, -1, -1, -1, -1, -1, -1, -1,		/* f8-ff */
};

static inline unsigned int hexval(unsigned char c)
{
	return hexval_table[c];
}

static inline int hex2chr(const char *s)
{
	int val = hexval(s[0]);
	return (val < 0) ? val : (val << 4) | hexval(s[1]);
}

static int packet_length(const char *packetlen)
{
	int val = hex2chr(packetlen);
	return (val < 0) ? val : (val << 8) | hex2chr(packetlen + 2);
}

static size_t packet_bin_read(void *buf, size_t count, FILE *stream)
{
	char packetlen[4];
	size_t len, ret;

	/* if we timeout waiting for input, exit and git will restart us if needed */
	size_t bytes_read = fread(packetlen, 1, 4, stream);
	if (0 == bytes_read)
	{
		exit(0);
	}
	if (4 != bytes_read)
	{
		die(-1, "invalid packet length");
	}

	len = packet_length(packetlen);
	if (!len)
	{
		return 0;
	}
	if (len < 4)
	{
		die(-1, "protocol error: bad line length character: %.4s", packetlen);
	}
	len -= 4;
	if (len >= count)
	{
		die(-1, "protocol error: bad line length %zu", len);
	}
	ret = fread(buf, 1, len, stream);
	if (ret != len)
	{
		die(-1, "invalid packet (%zu bytes expected; %zu bytes read)", len, ret);
	}

	return len;
}

size_t packet_txt_read(char *buf, size_t count, FILE *stream)
{
	size_t len;
	
	len = packet_bin_read(buf, count, stream);
	if (len && buf[len - 1] == '\n')
	{
		len--;
	}

	buf[len] = 0;
	return len;
}

void packet_txt_write(const char *buf, FILE *stream)
{
	char packetlen[4];
	size_t len, count = strlen(buf);

	set_packet_header(packetlen, count + 5);
	len = fwrite(packetlen, 1, 4, stream);
	if (len != 4)
	{
		die(-1, "error writing packet length");
	}
	len = fwrite(buf, 1, count, stream);
	if (len != count)
	{
		die(-1, "error writing packet");
	}
	len = fwrite("\n", 1, 1, stream);
	if (len != 1)
	{
		die(-1, "error writing packet");
	}
	fflush(stream);
}

void packet_flush(FILE *stream)
{
	size_t len;

	len = fwrite("0000", 1, 4, stream);
	if (len != 4)
	{
		die(-1, "error writing flush packet");
	}
	fflush(stream);
}
