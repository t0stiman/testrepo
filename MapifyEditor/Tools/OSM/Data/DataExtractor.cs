using Mapify.Editor.Utils;
using OsmSharp;
using OsmSharp.Complete;
using OsmSharp.Streams;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace Mapify.Editor.Tools.OSM.Data
{
    public class DataExtractor : MonoBehaviour, ISerializationCallbackReceiver
    {
        public enum ColourMode
        {
            Random,
            WayType
        }

        public enum Filter
        {
            All,
            CustomCode,
            CustomInspector,
            Railway,
            Building,
            Highway
        }

        // Original file and location.
        [SerializeField]
        public string OsmFile = ".osm";
        [SerializeField]
        public double Latitude = 51.115833;
        [SerializeField]
        public double Longitude = 6.218056;
        [SerializeField]
        public Vector3 OriginOffset = Vector3.zero;

        // Extra component workings.
        [SerializeField]
        public bool FilterNodesNotInWays = true;
        [SerializeField]
        public bool AlwaysDraw = false;
        [SerializeField]
        public bool DrawEveryNode = false;
        [SerializeField]
        public ColourMode ColouringMode = ColourMode.WayType;
        [SerializeField]
        public Filter CurrentFilter = Filter.All;
        [SerializeField]
        public Func<OsmGeo, bool> CustomFilter = All;

        // The usable data from the original file.
        public Dictionary<long, NodeVector3> NodeData = new Dictionary<long, NodeVector3>();
        public Dictionary<long, Way> WayData = new Dictionary<long, Way>();

        // Internals for the serialization.
        [SerializeField]
        private NodeVector3[] _nodes = new NodeVector3[0];
        [SerializeField]
        private Way[] _ways = new Way[0];

        public bool HasData => NodeData.Count > 0;

        private bool UseWayFilter => FilterNodesNotInWays && CurrentFilter != Filter.All;

        private void OnDrawGizmos()
        {
            if (AlwaysDraw)
            {
                Draw();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!AlwaysDraw)
            {
                Draw();
            }
        }

        private void Draw()
        {
            NodeVector3 here;
            NodeVector3 prev;
            float size = HandleUtility.GetHandleSize(Vector3.zero) * 0.01f;
            size = Mathf.Sqrt(size * size + 1.0f);

            foreach (var way in WayData.Values)
            {
                // Select the colour.
                switch (ColouringMode)
                {
                    case ColourMode.WayType:
                        Gizmos.color = GetColour(way);
                        break;
                    case ColourMode.Random:
                        Gizmos.color = Color.HSVToRGB(Math.Abs(way.GetHashCode()) / (float)int.MaxValue, 1, 1);
                        //Handles.color = UnityEngine.Random.ColorHSV(0, 1, 1, 1, 1, 1);
                        break;
                    default:
                        break;
                }

                Handles.color = Gizmos.color;

                // Start and end of way.
                Handles.DrawSolidDisc(NodeData[way.Nodes[0]].Position, Vector3.up, 4f * size);
                Handles.DrawWireDisc(NodeData[way.Nodes[way.Nodes.Length - 1]].Position, Vector3.up, 5f * size);

                here = NodeData[way.Nodes[0]];

                for (int i = 1; i < way.Nodes.Length; i++)
                {
                    prev = here;
                    here = NodeData[way.Nodes[i]];
                    // Line connecting the points.
                    Gizmos.DrawLine(prev.Position, here.Position);
                    DrawSpecificGizmo(here, size);

                    // Draw every single node in this case.
                    if (DrawEveryNode)
                    {
                        Handles.DrawWireDisc(here.Position, Vector3.up, 2f * size);
                    }
                }
            }

            Handles.color = Color.white;
        }

        private void DrawSpecificGizmo(NodeVector3 node, float size)
        {
            if (node.Tags != null && node.Tags.ContainsKey("railway", out int index))
            {
                switch (node.Tags[index].Value)
                {
                    case "buffer_stop":
                        Gizmos.DrawCube(node.Position, Vector3.one * 4f * size);
                        break;
                    case "switch":
                        Gizmos.DrawWireCube(node.Position, Vector3.one * 5f * size);
                        break;
                    case "turntable":
                        Gizmos.DrawSphere(node.Position, 4.0f);
                        break;
                    default:
                        break;
                }
            }
        }

        public void GenerateData()
        {
            switch (CurrentFilter)
            {
                case Filter.All:
                    GenerateData(All);
                    break;
                case Filter.CustomCode:
                    GenerateData(CustomFilter);
                    break;
                case Filter.Railway:
                    GenerateData(RailwayFilter);
                    break;
                case Filter.Building:
                    GenerateData(BuildingFilter);
                    break;
                case Filter.Highway:
                    GenerateData(HighwayFilter);
                    break;
                default:
                    break;
            }
        }

        public void GenerateData(Func<OsmGeo, bool> filter)
        {
            using (FileStream fileStream = File.OpenRead(OsmFile))
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                // Clear existing data to fit the new one.
                ClearData();

                // Create a source and filter.
                var source = new XmlOsmStreamSource(fileStream);
                var complete = source.Where(filter).ToComplete();

                // Create nodes.
                //object objLock = new object();
                //ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 8 };
                //Parallel.ForEach(complete, options, node =>
                //{
                //    if (node.Type == OsmGeoType.Node)
                //    {
                //        lock (objLock)
                //        {
                //            NodeData.Add(node.Id, new NodeVector3((Node)node, Latitude, Longitude, OriginOffset));
                //        }
                //    }
                //});

                NodeData = complete.Where(x => x.Type == OsmGeoType.Node).ToDictionary(
                    x => x.Id, x => new NodeVector3((Node)x, Latitude, Longitude, OriginOffset));

                // Create ways.
                var ways = complete.Where(x => x.Type == OsmGeoType.Way).Select(x => (CompleteWay)x);

                int skips = 0;

                // Hash set of the nodes found in ways for fast access.
                HashSet<long> nodesInWays = new HashSet<long>();

                foreach (var way in ways)
                {
                    // If a way doesn't actually link at least 2 points for some reason, skip it.
                    if (way.Nodes.Count() < 2)
                    {
                        skips++;
                        continue;
                    }

                    // Grab the node ids.
                    long[] wayNodes = way.Nodes.Select(x => x.Id.Value).ToArray();

                    // Add an entry for this way with some info and the nodes.
                    WayData.Add(way.Id, new Way(way) { Nodes = wayNodes });

                    if (UseWayFilter)
                    {
                        // Union with the new nodes, so there are no duplicates.
                        nodesInWays.UnionWith(wayNodes);
                    }
                }

                if (UseWayFilter)
                {
                    // For each node in the total nodes, check if there's a way that includes it.
                    // If there is, keep the node.
                    NodeData = NodeData.Where(x => nodesInWays.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);

                    // Old, very low performance.
                    //NodeData = NodeData.Where(x => WayData.Any(y => y.Value.Nodes.Any(z => z == x.Value.Id))).ToDictionary(x => x.Key, x => x.Value);
                }

                sw.Stop();
                Debug.Log($"Ways: {WayData.Count} | Skipped ways: {skips} | Total nodes: {NodeData.Count} | Filtered nodes: {nodesInWays.Count} | " +
                    $"Total time to process: {sw.Elapsed.TotalSeconds:F3}s");

                // Update the scene views with the new drawings.
                SceneView.RepaintAll();
            }
        }

        public void ClearData()
        {
            NodeData.Clear();
            WayData.Clear();
            SceneView.RepaintAll();
        }

        // Unity can't serialise dictionaries, and getting the nodes from arrays that may have hundreds or thousands
        // of values is not very performance friendly, so these convert the dictionaries to arrays and viceversa.
        public void OnBeforeSerialize()
        {
            _nodes = NodeData.Values.ToArray();
            _ways = WayData.Values.ToArray();
        }

        public void OnAfterDeserialize()
        {
            NodeData.Clear();
            WayData.Clear();
            NodeData = _nodes.ToDictionary(x => x.Id, x => x);
            WayData = _ways.ToDictionary(x => x.Id, x => x);
        }

        private Vector3[] NodesToPositions(long[] nodes)
        {
            return nodes.Select(x => NodeData[x].Position).ToArray();
        }

        #region FILTERS

        public static bool All(OsmGeo geo)
        {
            return true;
        }

        public static bool RailwayFilter(OsmGeo geo)
        {
            return geo.Type == OsmGeoType.Node ||
                    geo.Type == OsmGeoType.Way && geo.Tags != null && geo.Tags.ContainsKey("railway");
        }

        public static bool BuildingFilter(OsmGeo geo)
        {
            return geo.Type == OsmGeoType.Node ||
                    geo.Type == OsmGeoType.Way && geo.Tags != null && geo.Tags.ContainsKey("building");
        }

        public static bool HighwayFilter(OsmGeo geo)
        {
            return geo.Type == OsmGeoType.Node ||
                    geo.Type == OsmGeoType.Way && geo.Tags != null && geo.Tags.ContainsKey("highway");
        }

        #endregion

        #region COLOURS

        public static Color NoTags => new Color(0, 0, 0, 0.3f);
        public static Color Aerialway => new Color(0.10f, 0.10f, 0.10f);
        public static Color Aeroway => new Color(0.40f, 0.30f, 0.40f);
        public static Color Amenity => new Color(0.20f, 0.90f, 1.00f);
        public static Color Barrier => new Color(0.10f, 0.20f, 0.10f);
        public static Color Boundary => new Color(0.20f, 0.10f, 0.20f);
        public static Color Building => new Color(0.10f, 0.20f, 0.30f);
        public static Color Craft => new Color(0.70f, 0.70f, 0.00f);
        public static Color Emergency => new Color(1.00f, 0.10f, 0.10f);
        public static Color Geological => new Color(0.30f, 0.60f, 0.10f);
        public static Color Healthcare => new Color(1.00f, 0.40f, 0.80f);
        public static Color Highway => new Color(0.90f, 0.90f, 0.90f);
        public static Color Historic => new Color(0.90f, 0.70f, 0.40f);
        public static Color Landuse => new Color(0.70f, 0.70f, 0.70f);
        public static Color Leisure => new Color(0.20f, 0.90f, 0.60f);
        public static Color ManMade => new Color(0.70f, 0.10f, 0.80f);
        public static Color Military => new Color(0.30f, 0.90f, 0.10f);
        public static Color Natural => new Color(0.30f, 1.00f, 0.50f);
        public static Color Office => new Color(0.20f, 0.70f, 0.70f);
        public static Color Place => new Color(0.90f, 0.00f, 0.00f);
        public static Color Power => new Color(0.60f, 0.60f, 0.40f);
        public static Color PublicTransport => new Color(0.80f, 0.80f, 1.00f);
        public static Color Railway => new Color(0.40f, 0.40f, 0.40f);
        public static Color Route => new Color(0.60f, 0.60f, 0.60f);
        public static Color Shop => new Color(1.00f, 1.00f, 0.20f);
        public static Color Sport => new Color(0.30f, 0.90f, 0.90f);
        public static Color Telecom => new Color(0.90f, 0.10f, 0.30f);
        public static Color Tourism => new Color(0.00f, 0.00f, 0.00f);
        public static Color Water => new Color(0.30f, 0.80f, 1.00f);
        public static Color Waterway => new Color(0.40f, 0.95f, 1.00f);

        public static Color GetColour(Way way)
        {
            if (way.Tags == null)
            {
                // Ways with no tags are drawn as transparent.
                return NoTags;
            }
            else if (way.Tags.ContainsKey("aerialway"))
            {
                return Aerialway;
            }
            else if (way.Tags.ContainsKey("aeroway"))
            {
                return Aeroway;
            }
            else if (way.Tags.ContainsKey("amenity"))
            {
                return Amenity;
            }
            else if (way.Tags.ContainsKey("barrier"))
            {
                return Barrier;
            }
            else if (way.Tags.ContainsKey("boundary"))
            {
                return Boundary;
            }
            else if (way.Tags.ContainsKey("building"))
            {
                return Building;
            }
            else if (way.Tags.ContainsKey("craft"))
            {
                return Craft;
            }
            else if (way.Tags.ContainsKey("emergency"))
            {
                return Emergency;
            }
            else if (way.Tags.ContainsKey("geolocical"))
            {
                return Geological;
            }
            else if (way.Tags.ContainsKey("healthcare"))
            {
                return Healthcare;
            }
            else if (way.Tags.ContainsKey("highway"))
            {
                return Highway;
            }
            else if (way.Tags.ContainsKey("historic"))
            {
                return Historic;
            }
            else if (way.Tags.ContainsKey("landuse"))
            {
                return Landuse;
            }
            else if (way.Tags.ContainsKey("leisure"))
            {
                return Leisure;
            }
            else if (way.Tags.ContainsKey("man_made"))
            {
                return ManMade;
            }
            else if (way.Tags.ContainsKey("military"))
            {
                return Military;
            }
            else if (way.Tags.ContainsKey("natural"))
            {
                return Natural;
            }
            else if (way.Tags.ContainsKey("office"))
            {
                return Office;
            }
            else if (way.Tags.ContainsKey("place"))
            {
                return Place;
            }
            else if (way.Tags.ContainsKey("power"))
            {
                return Power;
            }
            else if (way.Tags.ContainsKey("public_transport"))
            {
                return PublicTransport;
            }
            else if (way.Tags.ContainsKey("railway"))
            {
                return Railway;
            }
            else if (way.Tags.ContainsKey("route"))
            {
                return Route;
            }
            else if (way.Tags.ContainsKey("shop"))
            {
                return Shop;
            }
            else if (way.Tags.ContainsKey("sport"))
            {
                return Sport;
            }
            else if (way.Tags.ContainsKey("telecom"))
            {
                return Telecom;
            }
            else if (way.Tags.ContainsKey("tourism"))
            {
                return Tourism;
            }
            else if (way.Tags.ContainsKey("water"))
            {
                return Water;
            }
            else if (way.Tags.ContainsKey("waterway"))
            {
                return Waterway;
            }
            else
            {
                return Color.black;
            }
        }

        #endregion
    }
}
#endif
