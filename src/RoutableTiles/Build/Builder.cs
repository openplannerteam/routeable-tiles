using System;
using System.Collections.Generic;
using OsmSharp.Streams;
using RoutableTiles.Build.Indexes;
using Serilog;
using RoutableTiles.IO;
using RoutableTiles.Tiles;

namespace RoutableTiles.Build
{

    /// <summary>
    /// Builds a database from scratch and writes the structure to the given folder.
    /// </summary>
    public static class Builder
    {
        /// <summary>
        /// Builds a new database and write the structure to the given path.
        /// </summary>
        public static void Build(OsmStreamSource source, string path, uint maxZoom = 14)
        {
            if (source == null) { throw new ArgumentNullException(nameof(source)); }
            if (path == null) { throw new ArgumentNullException(nameof(path)); }
            if (!source.CanReset) { throw new ArgumentException("Source cannot be reset."); }
            if (!FileSystemFacade.FileSystem.DirectoryExists(path)) { throw new ArgumentException("Output path does not exist."); }
            
            var tiles = BuildInitial(source, path, maxZoom, new Tile(0, 0, 0));
            while (true)
            {
                var newTiles = new List<Tile>();

                // System.Threading.Tasks.Parallel.ForEach(tiles, (subTile) =>
                // {
                //     var subTiles = Build(path, maxZoom, subTile);

                //     lock (newTiles)
                //     {
                //         newTiles.AddRange(subTiles);
                //     }
                // });

                foreach (var subTile in tiles)
                {
                   var subTiles = Build(path, maxZoom, subTile);

                   lock (newTiles)
                   {
                       newTiles.AddRange(subTiles);
                   }
                }

                if (newTiles.Count == 0)
                {
                    break;
                }

                tiles = newTiles;
            }
        }

        private static List<Tile> BuildInitial(OsmStreamSource source, string path, uint maxZoom, Tile tile)
        {
            Log.Logger.Information("Building for tile {0}/{1}/{2}...", tile.Zoom, tile.X, tile.Y);

            // split nodes and return nodes index and non-empty tiles.
            var nodeIndex = NodeProcessor.Process(source, path, maxZoom, tile, out var nonEmptyTiles, out var hasNext);

            // split ways using the node index and return the way index.
            Index wayIndex = null;
            if (hasNext)
            {
                wayIndex = WayProcessor.Process(source, path, maxZoom, tile, nodeIndex);
            }

            // split relations using the node and way index and return the relation index.
            var relationIndex = RelationProcessor.Process(source, path, maxZoom, tile, nodeIndex, wayIndex);

            // write the indices to disk.
            nodeIndex.Write(FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToString(),
                    tile.X.ToString(), tile.Y.ToString() + ".nodes.idx"));
            wayIndex?.Write(FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToString(),
                tile.X.ToString(), tile.Y.ToString() + ".ways.idx"));
            relationIndex.Write(FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToString(),
                    tile.X.ToString(), tile.Y.ToString() + ".relations.idx"));

            return nonEmptyTiles;
        }
        
        /// <summary>
        /// Builds the database and writes the structure to the given by by splitting the given zoom level.
        /// </summary>
        private static List<Tile> Build(string path, uint maxZoom, Tile tile)
        {
            Log.Logger.Information("Building for tile {0}/{1}/{2}...", tile.Zoom, tile.X, tile.Y);

            // split nodes and return index and non-empty tiles.
            List<Tile> nonEmptyTiles = null;
            Index nodeIndex = null;
            var nodeFile = FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToString(),
                tile.X.ToString(), tile.Y.ToString() + ".nodes.osm.bin");
            if (!FileSystemFacade.FileSystem.Exists(nodeFile))
            {
                Log.Logger.Warning("Tile {0}/{1}/{2} not found: {3}", tile.Zoom, tile.X, tile.Y,
                    nodeFile);
                return new List<Tile>();
            }
            using (var nodeStream = FileSystemFacade.FileSystem.OpenRead(nodeFile))
            {
                var nodeSource = new OsmSharp.Streams.BinaryOsmStreamSource(nodeStream);

                // split nodes and return nodes index and non-empty tiles.
                nodeIndex = NodeProcessor.Process(nodeSource, path, maxZoom, tile, out nonEmptyTiles,
                    out _);
            }

            // build the ways index.
            Index wayIndex = null;
            var wayFile = FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToString(),
                tile.X.ToString(), tile.Y.ToString() + ".ways.osm.bin");
            if (FileSystemFacade.FileSystem.Exists(wayFile))
            {
                using (var wayStream = FileSystemFacade.FileSystem.OpenRead(wayFile))
                {
                    var waySource = new OsmSharp.Streams.BinaryOsmStreamSource(wayStream);
                    if (waySource.MoveNext())
                    {
                        wayIndex = WayProcessor.Process(waySource, path, maxZoom, tile, nodeIndex);
                    }
                }
            }  

            // build the relations index.
            Index relationIndex = null;
            var relationFile = FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToString(),
                tile.X.ToString(), tile.Y.ToString() + ".relations.osm.bin");
            if (FileSystemFacade.FileSystem.Exists(relationFile))
            {
                using (var relationStream = FileSystemFacade.FileSystem.OpenRead(relationFile))
                {
                    var relationSource = new OsmSharp.Streams.BinaryOsmStreamSource(relationStream);
                    if (relationSource.MoveNext())
                    {
                        relationIndex = RelationProcessor.Process(relationSource, path, maxZoom, tile, nodeIndex, wayIndex);
                    }
                }
            }

            // write the indexes to disk.
            nodeIndex.Write(FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToString(),
                    tile.X.ToString(), tile.Y.ToString() + ".nodes.idx"));
            wayIndex?.Write(FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToString(),
                tile.X.ToString(), tile.Y.ToString() + ".ways.idx"));
            relationIndex?.Write(FileSystemFacade.FileSystem.Combine(path, tile.Zoom.ToString(),
                tile.X.ToString(), tile.Y.ToString() + ".relations.idx"));

            if (FileSystemFacade.FileSystem.Exists(nodeFile))
            {
                FileSystemFacade.FileSystem.Delete(nodeFile);
            }
            if (FileSystemFacade.FileSystem.Exists(wayFile))
            {
                FileSystemFacade.FileSystem.Delete(wayFile);
            }
            if (FileSystemFacade.FileSystem.Exists(relationFile))
            {
                FileSystemFacade.FileSystem.Delete(relationFile);
            }

            return nonEmptyTiles;
        }
    }
}