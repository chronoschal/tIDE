﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TileMapEditor.Plugin
{
    public interface IApplication
    {
        IMenuStrip MenuStrip { get; }
    }
}
