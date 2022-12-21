﻿/**
 * Houdini Geo File Importer for Unity
 *
 * Copyright 2015 by Waldo Bronchart <wbronchart@gmail.com>
 * Exporter added in 2021 by Roy Theunissen <roy.theunissen@live.nl>
 * Licensed under GNU General Public License 3.0 or later. 
 * Some rights reserved. See COPYING, AUTHORS.
 */

using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Houdini.GeoImportExport
{
    [Serializable]
    public class HoudiniGeoFileInfo
    {
        public DateTime date;
        public float timetocook;
        public string software;
        public string artist;
        public string hostname;
        public float time; // TODO: What is this for? It was missing but I just see it being 0 in GEO files.
        public Bounds bounds;
        public string primcount_summary;
        public string attribute_summary;
        public string group_summary;

        public HoudiniGeoFileInfo Copy()
        {
            return (HoudiniGeoFileInfo)MemberwiseClone();
        }
    }

    public enum HoudiniGeoAttributeType
    {
        Invalid = 0,
        Float,
        Integer,
        String,
    }

    public enum HoudiniGeoAttributeOwner
    {
        Invalid = 0,
        Vertex,
        Point,
        Primitive,
        Detail,
        Any,
    }

    [Serializable]
    public class HoudiniGeoAttribute
    {
        public string name;
        public HoudiniGeoAttributeType type;
        public HoudiniGeoAttributeOwner owner;
        public int tupleSize;

        public List<float> floatValues = new List<float>();
        public List<int> intValues = new List<int>();
        public List<string> stringValues = new List<string>();
    }



    [Serializable]
    public abstract class HoudiniGeoPrimitive
    {
        public string type;
        public int id;
    }
    
    [Serializable]
    public class PolyPrimitive : HoudiniGeoPrimitive
    {
        public int[] indices;
        public int[] triangles;

        public PolyPrimitive()
        {
            type = "Poly";
        }
    }
    
    [Serializable]
    public class BezierCurvePrimitive : HoudiniGeoPrimitive
    {
        public int[] indices;
        public int order;
        public int[] knots;
        
        public BezierCurvePrimitive()
        {
            type = "BezierCurve";
        }
    }
    
    [Serializable]
    public class NURBCurvePrimitive : HoudiniGeoPrimitive
    {
        public int[] indices;
        public int order;
        public bool endInterpolation;
        public int[] knots;
        
        public NURBCurvePrimitive()
        {
            type = "NURBCurve";
        }
    }






    public enum HoudiniGeoGroupType
    {
        Invalid = 0,
        Points,
        Primitives,
        Edges,
    }

    public class HoudiniGeoGroup
    {
        public string name;
        public HoudiniGeoGroupType type;

        public HoudiniGeoGroup(string name, HoudiniGeoGroupType type)
        {
            this.name = name;
            this.type = type;
        }
    }

    public class PrimitiveGroup : HoudiniGeoGroup
    {
        public List<int> ids;

        public PrimitiveGroup(string name, List<int> ids) : base(name, HoudiniGeoGroupType.Primitives)
        {
            this.ids = ids;
        }
    }
    
    public class PointGroup : HoudiniGeoGroup
    {
        public List<int> ids;
        public List<int> vertIds;

        public PointGroup(string name, List<int> ids, List<int> vertIds) : base(name, HoudiniGeoGroupType.Points)
        {
            this.ids = ids;
            this.vertIds = vertIds;
        }
    }
    
    public class EdgeGroup : HoudiniGeoGroup
    {
        public List<KeyValuePair<int, int>> pointPairs;

        public EdgeGroup(string name, List<KeyValuePair<int, int>> pointPairs) : base(name, HoudiniGeoGroupType.Edges)
        {
            this.pointPairs = pointPairs;
        }
    }

    [Serializable]
    public class HoudiniGeoImportSettings
    {
        public bool reverseWinding;
    }

    public class HoudiniGeo : ScriptableObject
    {
        public const string EXTENSION = "geo";
        
        public const string POS_ATTR_NAME = "P";
        public const string NORMAL_ATTR_NAME = "N";
        public const string COLOR_ATTR_NAME = "Cd";
        public const string ALPHA_ATTR_NAME = "Alpha";
        public const string UV_ATTR_NAME = "uv";
        public const string UV2_ATTR_NAME = "uv2";
        public const string TANGENT_ATTR_NAME = "tangent";
        public const string MATERIAL_ATTR_NAME = "shop_materialpath";
        public const string DEFAULT_MATERIAL_NAME = "Default";

        public UnityEngine.Object sourceAsset;

        public HoudiniGeoImportSettings importSettings;

        public string fileVersion;
        public bool hasIndex;
        public int pointCount;
        public int vertexCount;
        public int primCount;
        public HoudiniGeoFileInfo fileInfo;

        public List<int> pointRefs = new List<int>();
            
        public List<HoudiniGeoAttribute> attributes = new List<HoudiniGeoAttribute>();

        public PolyPrimitive[] polyPrimitives = new PolyPrimitive[0];
        public BezierCurvePrimitive[] bezierCurvePrimitives = new BezierCurvePrimitive[0];
        public NURBCurvePrimitive[] nurbCurvePrimitives = new NURBCurvePrimitive[0];

        public List<PrimitiveGroup> primitiveGroups = new List<PrimitiveGroup>();
        public List<PointGroup> pointGroups = new List<PointGroup>();
        public List<EdgeGroup> edgeGroups = new List<EdgeGroup>();

        [HideInInspector] public string exportPath;
        
        public delegate void GeoFileImportedHandler(HoudiniGeo houdiniGeo);
        public static event GeoFileImportedHandler GeoFileImportedEvent;
        
        public static HoudiniGeo Create()
        {
            HoudiniGeo geo = CreateInstance<HoudiniGeo>();
            
            // Populate it with some default info.
            geo.fileVersion = "18.5.408";
            geo.fileInfo = new HoudiniGeoFileInfo
            {
                date = DateTime.Now,
                software = "Unity " + Application.unityVersion,
                artist = Environment.UserName,
                hostname = Environment.MachineName,
            };

            return geo;
        }

        public void Clear()
        {
            pointCount = 0;
            pointRefs = new List<int>();
            
            attributes.Clear();
            polyPrimitives = new PolyPrimitive[0];
            bezierCurvePrimitives = new BezierCurvePrimitive[0];
            nurbCurvePrimitives = new NURBCurvePrimitive[0];
            primitiveGroups.Clear();
            pointGroups.Clear();
            edgeGroups.Clear();
        }

        public static void DispatchGeoFileImportedEvent(HoudiniGeo houdiniGeo)
        {
            GeoFileImportedEvent?.Invoke(houdiniGeo);
        }
    }
}
