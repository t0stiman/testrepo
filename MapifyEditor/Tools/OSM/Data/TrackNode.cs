using Mapify.Editor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Mapify.Editor.Tools.OSM.Data
{
    [Serializable]
    public class TrackNode
    {
        // Type of node, based on number of connections.
        public enum NodeType
        {
            Empty       = 0,
            End         = 1,
            Connected   = 2,
            Switch      = 3,
            Cross       = 4,
            Over4       = 5
        }

        // For switch nodes, whether to use the left or right switch prefabs.
        public enum SwitchOrientation
        {
            Undefined,
            Left,
            Right
        }

        public long Id;
        public string Name;
        public Vector3 Position;
        public NodeTag[] Tags;
        public List<TrackNode> Connected;
        public SwitchOrientation Orientation;

        private List<TrackNodeHandle> _handles;

        public TrackNode(long id, string name, Vector3 position, NodeTag[] tags)
        {
            Id = id;
            Name = name;
            Position = position;
            Tags = tags;
            Connected = new List<TrackNode>();
            Orientation = SwitchOrientation.Undefined;

            _handles = new List<TrackNodeHandle>();
        }

        public TrackNode(NodeVector3 node) : this(node.Id, node.Name, node.Position, node.Tags) { }
        // Empty constructor for serialization.
        public TrackNode() : this(-1, "", Vector3.zero, new NodeTag[0]) { }

        public NodeType GetNodeType()
        {
            switch (Connected.Count)
            {
                case 0:
                    return NodeType.Empty;
                case 1:
                    return NodeType.End;
                case 2:
                    return NodeType.Connected;
                case 3:
                    return NodeType.Switch;
                case 4:
                    return NodeType.Cross;
                default:
                    return NodeType.Over4;
            }
        }

        public Vector3 GetHandle(int index)
        {
            if (index < 0)
            {
                return Vector3.zero;
            }

            return _handles[index].FullHandle;
        }

        public Vector3 GetHandle(TrackNode node)
        {
            int index = GetIndex(node);

            return GetHandle(index);
        }

        public Vector3 GetGlobalHandle(int index)
        {
            if (index < -1)
            {
                return Vector3.zero;
            }

            return Position + GetHandle(index);
        }

        public Vector3 GetGlobalHandle(TrackNode node)
        {
            return GetGlobalHandle(GetIndex(node));
        }

        public int GetIndex(TrackNode node)
        {
            for (int i = 0; i < _handles.Count; i++)
            {
                if (Connected[i].Id == node.Id)
                {
                    return i;
                }
            }

            return -1;
        }

        public void TryConnect(TrackNode node)
        {
            // Connect if it hasn't been connected before.
            if (!Connected.Any(x => x.Id == node.Id))
            {
                Connected.Add(node);
                // Do the same for the connected node, so the connection is both ways.
                node.TryConnect(this);
            }
        }

        public NodeType CalculateHandles(bool sameLength)
        {
            NodeType nodeType = GetNodeType();
            _handles.Clear();

            switch (nodeType)
            {
                case NodeType.Empty:
                    Debug.LogWarning($"Node {Id}:{Position} is empty.");
                    break;
                case NodeType.End:
                    // Do not point directly to the other node if it has a handle, instead
                    // try to smooth it. If there's no handle it will be a straight line.
                    Vector3 target = Connected[0].Position + Connected[0].GetHandle(this) * 1.5f;
                    Vector3 dif = Connected[0].Position - Position;
                    _handles.Add(new TrackNodeHandle((target - Position).normalized, dif.magnitude * MathHelper.OneThird));
                    break;
                case NodeType.Connected:
                    // Smooth.
                    var handles = MathHelper.GetSizedSmoothHandles(Connected[0].Position, Position, Connected[1].Position);
                    _handles.Add(handles.Next);
                    _handles.Add(handles.Prev);
                    break;
                case NodeType.Switch:
                    // These handles are a special case, as such they are just the directions to the
                    // connected points instead of proper handles. The actual handles are calculated
                    // using these and the switch instance's positions.
                    _handles.Add(new TrackNodeHandle((Connected[0].Position - Position).normalized, 1));
                    _handles.Add(new TrackNodeHandle((Connected[1].Position - Position).normalized, 1));
                    _handles.Add(new TrackNodeHandle((Connected[2].Position - Position).normalized, 1));

                    // Check which node pair is the straightest line.
                    float dot01 = Vector3.Dot(_handles[0].Direction, _handles[1].Direction);
                    float dot12 = Vector3.Dot(_handles[1].Direction, _handles[2].Direction);
                    float dot20 = Vector3.Dot(_handles[2].Direction, _handles[0].Direction);

                    if (dot12 < dot01 && dot12 < dot20)
                    {
                        // 1 and 2 are the straightest, so 0 is the diverging track.
                        SwitchSwap(Connected[1], Connected[2], Connected[0]);
                    }
                    else if (dot20 < dot01 && dot20 < dot12)
                    {
                        // 2 and 0 are the straightest, so 1 is the diverging track.
                        SwitchSwap(Connected[2], Connected[0], Connected[1]);
                    }
                    // No need to switch if 0 and 1 are the straightest pair (2 is diverging).

                    // If the diverging exit is closer to the join point instead of the through exit,
                    // swap them around.
                    // Good: 2 1  1 2   Bad:  1  1  
                    //        \|  |/          |  |  
                    //         |  |          /|  |\ 
                    //         0  0         2 0  0 2
                    if (Vector3.Dot(_handles[2].Direction, _handles[0].Direction) > 0)
                    {
                        SwitchSwap(Connected[1], Connected[0], Connected[2]);
                    }

                    // Cross through direction with the exit direction to see if it's to the left or right.
                    Vector3 direction = MathHelper.AverageDirection(-_handles[0].Direction, _handles[1].Direction);
                    Vector3 cross = Vector3.Cross(direction, _handles[2].Direction);
                    Orientation = cross.y < 0 ? SwitchOrientation.Left : SwitchOrientation.Right;

                    // Set the first handle to match the switch's final orientation, and use it when instancing.
                    // Switches need to be flat so also do that.
                    _handles[0].Direction = FlattenNoResize(direction).normalized;
                    break;
                case NodeType.Cross:
                    Debug.LogWarning($"Node {Id}:{Position} is a cross, not implemented yet.");
                    _handles.Add(new TrackNodeHandle((Connected[0].Position - Position).normalized, 1));
                    _handles.Add(new TrackNodeHandle((Connected[1].Position - Position).normalized, 1));
                    _handles.Add(new TrackNodeHandle((Connected[2].Position - Position).normalized, 1));
                    _handles.Add(new TrackNodeHandle((Connected[3].Position - Position).normalized, 1));
                    break;
                case NodeType.Over4:
                    Debug.LogWarning($"Node {Id}:{Position} has more than 4 connections.");

                    for (int i = 0; i < Connected.Count; i++)
                    {
                        _handles.Add(new TrackNodeHandle((Connected[i].Position - Position).normalized, 1));
                    }
                    break;
                default:
                    break;
            }

            return nodeType;
        }

        private void SwitchSwap(TrackNode into, TrackNode through, TrackNode diverging)
        {
            Connected[0] = into;
            Connected[1] = through;
            Connected[2] = diverging;

            _handles.Clear();
            _handles.Add(new TrackNodeHandle((Connected[0].Position - Position).normalized, 1));
            _handles.Add(new TrackNodeHandle((Connected[1].Position - Position).normalized, 1));
            _handles.Add(new TrackNodeHandle((Connected[2].Position - Position).normalized, 1));
        }

        public static Vector3 FlattenNoResize(Vector3 handle)
        {
            handle.y = 0;
            return handle;
        }

        public static Vector3 Flatten(Vector3 handle)
        {
            float length = handle.magnitude;
            handle.y = 0;
            return handle.normalized * length;
        }
    }
}
