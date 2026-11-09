/* The RNNoise 0.2 release tarball includes "os_support.h" from vec.h and vec_neon.h (the paths
   taken on ARM, i.e. Apple Silicon) but does not ship it; on x86 the AVX/SSE path never needs it.
   This supplies the two macros those headers use, as Opus defines them. */
#ifndef OS_SUPPORT_H
#define OS_SUPPORT_H

#include <string.h>

#ifndef OPUS_INLINE
#define OPUS_INLINE inline
#endif

#define OPUS_CLEAR(dst, n) (memset((dst), 0, (n)*sizeof(*(dst))))
#define OPUS_COPY(dst, src, n) (memcpy((dst), (src), (n)*sizeof(*(dst)) + 0*((dst)-(src))))

#endif
