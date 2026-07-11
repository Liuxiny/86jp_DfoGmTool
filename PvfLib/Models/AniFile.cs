using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    public class AniFrame
    {
        public string Name { get; set; }
        public string ImagePath { get; set; }
        public int ImageIndex { get; set; } = -1;
        public int[] ImagePos { get; set; }
        public int Delay { get; set; } = -1;
        public int[] Rgba { get; set; }
        public int Interpolation { get; set; } = -1;
        public string GraphicEffect { get; set; }
    }

    
    
    
    
    public class AniFile : PvfModelBase
    {
        public int Loop { get; set; } = -1;
        public int FrameMax { get; set; } = -1;
        public int Shadow { get; set; } = -1;
        public List<AniFrame> Frames { get; set; } = new List<AniFrame>();

        public static AniFile Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new AniFile { Content = content ?? string.Empty, Root = new ScriptNode { Tag = "ROOT" } };

            var root = new ScriptParser().Parse(content);
            var ani = new AniFile { Root = root, Content = content };

            foreach (var node in root.Children)
            {
                string data = node.DataItems.Count > 0 ? node.GetFirstDataContent(content).Trim() : string.Empty;
                string tag = node.Tag.ToLowerInvariant();
                switch (tag)
                {
                    case "loop":
                        ani.Loop = ParseInt(data);
                        break;
                    case "frame max":
                        ani.FrameMax = ParseInt(data);
                        break;
                    case "shadow":
                        ani.Shadow = ParseInt(data);
                        break;
                    default:
                        if (tag.StartsWith("frame", StringComparison.Ordinal))
                            ani.Frames.Add(ParseFrame(node, content));
                        break;
                }
            }

            return ani;
        }

        private static AniFrame ParseFrame(ScriptNode node, string content)
        {
            var frame = new AniFrame { Name = node.Tag };
            foreach (var child in node.Children)
            {
                string data = child.DataItems.Count > 0 ? child.GetFirstDataContent(content).Trim() : string.Empty;
                switch (child.Tag.ToLowerInvariant())
                {
                    case "image":
                        ParseImageReference(data, frame);
                        break;
                    case "image pos":
                        frame.ImagePos = ParseIntArray(data);
                        break;
                    case "delay":
                        frame.Delay = ParseInt(data);
                        break;
                    case "rgba":
                        frame.Rgba = ParseIntArray(data);
                        break;
                    case "interpolation":
                        frame.Interpolation = ParseInt(data);
                        break;
                    case "graphic effect":
                        frame.GraphicEffect = StripBacktick(data);
                        break;
                }
            }
            return frame;
        }

        private static void ParseImageReference(string data, AniFrame frame)
        {
            if (string.IsNullOrWhiteSpace(data))
                return;

            int firstTick = data.IndexOf('`');
            int lastTick = data.LastIndexOf('`');
            if (firstTick >= 0 && lastTick > firstTick)
            {
                frame.ImagePath = data.Substring(firstTick + 1, lastTick - firstTick - 1);
                string tail = data.Substring(lastTick + 1).Trim();
                if (!string.IsNullOrEmpty(tail))
                    frame.ImageIndex = ParseInt(tail);
                return;
            }

            frame.ImagePath = StripBacktick(data);
        }
    }
}
