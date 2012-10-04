using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace encausse.net {
  class Streamer : MemoryStream {
    public override long Length {
      get { return -1L; }
    }

    public override int Read(byte[] buffer, int offset, int count) {
      int c = count;

      int r = base.Read(buffer, offset, count);
      while (r < count) {
        r += base.Read(buffer, offset + r, count - r);
      }
      return r;
    }
  }
}