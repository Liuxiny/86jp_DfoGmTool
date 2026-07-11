using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace GmPvfLib
{
    
    
    
    public class PvfTreeBuilder
    {
        private static readonly char[] Separators = { '/', '\\' };
        private static readonly char[] TrimChars = { '/', '\\' };

        public PvfTreeNode BuildTree(IList<PvfFileData> files)
        {
            var root = new PvfTreeNode { Name = "Root", FullPath = "", IsDirectory = true };
            if (files == null || files.Count == 0)
                return root;

            
            var pathCache = new Dictionary<string, PvfTreeNode>(8192, StringComparer.OrdinalIgnoreCase);
            var dirMap = new Dictionary<string, PvfTreeNode>(16384, StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder(256);

            foreach (var file in files)
            {
                string filePath = (file.Path ?? "").Replace('\\', '/');
                string fileName = (file.Name ?? "").Replace('\\', '/');
                if (fileName.Length >= 2 && fileName[0] == '.' && fileName[1] == '/')
                    fileName = fileName.Substring(2);

                
                PvfTreeNode parentDir;
                if (filePath.Length == 0)
                {
                    parentDir = root;
                }
                else if (!pathCache.TryGetValue(filePath, out parentDir))
                {
                    
                    string[] parts = filePath.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                    var current = root;
                    sb.Clear();

                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (sb.Length > 0) sb.Append('/');
                        sb.Append(parts[i]);
                        string pathSoFar = sb.ToString();

                        if (!dirMap.TryGetValue(pathSoFar, out var next))
                        {
                            next = new PvfTreeNode
                            {
                                Name = parts[i],
                                FullPath = pathSoFar,
                                IsDirectory = true
                            };
                            current.Children.Add(next);
                            dirMap[pathSoFar] = next;
                        }
                        current = next;
                    }

                    parentDir = current;
                    pathCache[filePath] = parentDir;
                }

                
                if (IsDirectoryEntry(fileName))
                {
                    string trimmed = fileName.TrimEnd(TrimChars);
                    string fullPath = parentDir == root ? trimmed : parentDir.FullPath + "/" + trimmed;
                    var dirNode = new PvfTreeNode
                    {
                        Name = trimmed,
                        FullPath = fullPath,
                        IsDirectory = true,
                        FileInfo = file
                    };
                    parentDir.Children.Add(dirNode);
                    dirMap[fullPath] = dirNode;
                }
                else
                {
                    string fullPath = parentDir == root ? fileName : parentDir.FullPath + "/" + fileName;
                    parentDir.Children.Add(new PvfTreeNode
                    {
                        Name = fileName,
                        FullPath = fullPath,
                        IsDirectory = false,
                        FileInfo = file
                    });
                }
            }

            return root;
        }

        
        
        
        public void SortTree(PvfTreeNode node)
        {
            if (node?.Children == null || node.Children.Count == 0)
                return;

            node.Children.Sort((a, b) =>
            {
                int cmp = b.IsDirectory.CompareTo(a.IsDirectory);
                return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var child in node.Children)
            {
                if (child.IsDirectory) SortTree(child);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDirectoryEntry(string name)
        {
            return !string.IsNullOrEmpty(name) && (name[name.Length - 1] == '/' || name[name.Length - 1] == '\\');
        }
    }
}
