﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tiling.Format
{
    public interface IMapFormat
    {
        CompatibilityResults DetermineCompatibility(Map map);

        Map Load(Stream stream);

        void Store(Map map, Stream stream);

        string Name { get; }

        string FileExtension { get; }
    }
}
