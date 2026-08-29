using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace NosAi.Navigation.Pathfinding;

public enum TileType : byte { Walkable, BlockedObstacle, WaterOrChasm, SafeZoneTown, PortalEntrance }
public enum NavigationStatus : byte { Idle, Navigating, WaypointReached, DestinationReached, StuckDetectedRerouting, PathNotFound }
public readonly record struct GridPoint(int X, int Y)
{
    public double DistanceTo(GridPoint other) { long dx = X - other.X, dy = Y - other.Y; return Math.Sqrt(dx * dx + dy * dy); }
    public int ManhattanDistanceTo(GridPoint other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
}
public sealed record MapPortal(string PortalId,int SourceMapId,GridPoint SourcePosition,int DestinationMapId,GridPoint DestinationPosition,string TargetMapName);
public sealed record DynamicHazardZone(long SourceEntityId,GridPoint Center,int RadiusTiles,float DangerWeightMultiplier);
public sealed record CalculatedPathResult(int MapId,GridPoint StartPoint,GridPoint TargetPoint,bool IsPathFound,ImmutableArray<GridPoint> Waypoints,double TotalPathCost,long ComputationTimeMs);

public sealed class MapGridData
{
    public int MapId { get; } public string MapName { get; } public int Width { get; } public int Height { get; }
    private readonly byte[] _tiles; private readonly float[] _hazardCostOverlay;
    public MapGridData(int mapId,string mapName,int width,int height)
    {
        if(width<=0||height<=0) throw new ArgumentOutOfRangeException(nameof(width));
        MapId=mapId; MapName=mapName??throw new ArgumentNullException(nameof(mapName)); Width=width; Height=height;
        _tiles=new byte[checked(width*height)]; _hazardCostOverlay=new float[_tiles.Length];
    }
    public bool IsWithinBounds(int x,int y)=>x>=0&&x<Width&&y>=0&&y<Height;
    public TileType GetTileType(int x,int y)=>!IsWithinBounds(x,y)?TileType.BlockedObstacle:(TileType)_tiles[y*Width+x];
    public void SetTileType(int x,int y,TileType type){if(IsWithinBounds(x,y))_tiles[y*Width+x]=(byte)type;}
    public bool IsWalkable(int x,int y)=>GetTileType(x,y) is TileType.Walkable or TileType.SafeZoneTown or TileType.PortalEntrance;
    public void ClearHazardOverlay()=>Array.Clear(_hazardCostOverlay);
    public void ApplyHazard(DynamicHazardZone hazard)
    {
        if(hazard.RadiusTiles<0||hazard.DangerWeightMultiplier<0) throw new ArgumentOutOfRangeException(nameof(hazard));
        int minX=Math.Max(0,hazard.Center.X-hazard.RadiusTiles),maxX=Math.Min(Width-1,hazard.Center.X+hazard.RadiusTiles);
        int minY=Math.Max(0,hazard.Center.Y-hazard.RadiusTiles),maxY=Math.Min(Height-1,hazard.Center.Y+hazard.RadiusTiles);
        int r2=checked(hazard.RadiusTiles*hazard.RadiusTiles);
        for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++){int dx=x-hazard.Center.X,dy=y-hazard.Center.Y;if(dx*dx+dy*dy<=r2)_hazardCostOverlay[y*Width+x]+=hazard.DangerWeightMultiplier;}
    }
    public float GetTraversalCost(int x,int y)=>!IsWalkable(x,y)?float.PositiveInfinity:1f+_hazardCostOverlay[y*Width+x];
}

public sealed class AStarPathfinder
{
    private static readonly (int dx,int dy,float cost)[] Directions={(0,1,1f),(1,0,1f),(0,-1,1f),(-1,0,1f),(1,1,1.41421356f),(1,-1,1.41421356f),(-1,1,1.41421356f),(-1,-1,1.41421356f)};
    public CalculatedPathResult FindPath(MapGridData map,GridPoint start,GridPoint target,bool allowDiagonal=true,int maxSteps=10000)
    {
        ArgumentNullException.ThrowIfNull(map); if(maxSteps<=0)throw new ArgumentOutOfRangeException(nameof(maxSteps)); var sw=Stopwatch.StartNew();
        if(!map.IsWalkable(start.X,start.Y)||!map.IsWalkable(target.X,target.Y))return Fail(map,start,target,sw);
        if(start==target)return new(map.MapId,start,target,true,ImmutableArray.Create(start),0,sw.ElapsedMilliseconds);
        var open=new PriorityQueue<GridPoint,float>();var came=new Dictionary<GridPoint,GridPoint>();var g=new Dictionary<GridPoint,float>{{start,0f}};open.Enqueue(start,H(start,target));int steps=0;
        while(open.Count>0&&steps++<maxSteps){var cur=open.Dequeue();if(cur==target){sw.Stop();return new(map.MapId,start,target,true,Reconstruct(came,cur),g[target],sw.ElapsedMilliseconds);}for(int i=0;i<(allowDiagonal?8:4);i++){var(dyX,dyY,move)=Directions[i];int nx=cur.X+dyX,ny=cur.Y+dyY;if(!map.IsWalkable(nx,ny))continue;if(dyX!=0&&dyY!=0&&(!map.IsWalkable(cur.X+dyX,cur.Y)||!map.IsWalkable(cur.X,cur.Y+dyY)))continue;var n=new GridPoint(nx,ny);float candidate=g[cur]+move*map.GetTraversalCost(nx,ny);if(!g.TryGetValue(n,out var old)||candidate<old){came[n]=cur;g[n]=candidate;open.Enqueue(n,candidate+H(n,target));}}}
        sw.Stop();return Fail(map,start,target,sw);
    }
    private static CalculatedPathResult Fail(MapGridData m,GridPoint s,GridPoint t,Stopwatch sw)=>new(m.MapId,s,t,false,ImmutableArray<GridPoint>.Empty,0,sw.ElapsedMilliseconds);
    private static float H(GridPoint a,GridPoint b){int dx=Math.Abs(a.X-b.X),dy=Math.Abs(a.Y-b.Y);return dx+dy+(1.41421356f-2f)*Math.Min(dx,dy);}
    private static ImmutableArray<GridPoint> Reconstruct(Dictionary<GridPoint,GridPoint> came,GridPoint cur){var list=new List<GridPoint>{cur};while(came.TryGetValue(cur,out var p)){cur=p;list.Add(cur);}list.Reverse();return list.ToImmutableArray();}
}

public sealed class PathSmoother
{
    public ImmutableArray<GridPoint> SmoothPath(MapGridData map,ImmutableArray<GridPoint> raw){if(raw.Length<=2)return raw;var result=new List<GridPoint>{raw[0]};int i=0;while(i<raw.Length-1){int furthest=i+1;for(int j=raw.Length-1;j>i+1;j--)if(HasLineOfSight(map,raw[i],raw[j])){furthest=j;break;}result.Add(raw[furthest]);i=furthest;}return result.ToImmutableArray();}
    public bool HasLineOfSight(MapGridData map,GridPoint start,GridPoint end){int x=start.X,y=start.Y,x1=end.X,y1=end.Y,dx=Math.Abs(x1-x),dy=Math.Abs(y1-y),sx=x<x1?1:-1,sy=y<y1?1:-1,err=dx-dy;while(true){if(!map.IsWalkable(x,y))return false;if(x==x1&&y==y1)return true;int e2=2*err;if(e2>-dy){err-=dy;x+=sx;}if(e2<dx){err+=dx;y+=sy;}}}
}

public sealed record MultiMapTransitLeg(int StepNumber,int CurrentMapId,string MapName,GridPoint WalkToPosition,MapPortal? UsePortalToNextMap);
public sealed class WorldMapPortalRouter
{
    private readonly List<MapPortal> _portals=new();private readonly Dictionary<int,string> _mapNames=new();
    public WorldMapPortalRouter(){_mapNames[1]="NosVille";_mapNames[2]="Prateria di NosVille";_mapNames[3]="Pianure di NosVille";_mapNames[4]="Miniera d'Oro Orientale";_mapNames[5]="Tempio Fernon 1P";AddBidirectionalPortal("PORTAL_NOS_PRA",1,new(140,20),2,new(10,80),"Prateria di NosVille");AddBidirectionalPortal("PORTAL_PRA_PIA",2,new(90,85),3,new(15,20),"Pianure di NosVille");AddBidirectionalPortal("PORTAL_PIA_MIN",3,new(120,110),4,new(20,30),"Miniera d'Oro Orientale");AddBidirectionalPortal("PORTAL_PIA_FER",3,new(80,140),5,new(30,20),"Tempio Fernon 1P");}
    public void AddBidirectionalPortal(string id,int map1,GridPoint pos1,int map2,GridPoint pos2,string targetName){_portals.Add(new($"{id}_FORWARD",map1,pos1,map2,pos2,targetName));_portals.Add(new($"{id}_BACKWARD",map2,pos2,map1,pos1,_mapNames.GetValueOrDefault(map1,$"Map_{map1}")));}
    public List<MultiMapTransitLeg>? PlanMultiMapRoute(int start,int destination){if(start==destination)return[new(1,start,_mapNames.GetValueOrDefault(start,"CurrentMap"),new(0,0),null)];var prev=new Dictionary<int,MapPortal>();var dist=new Dictionary<int,int>{{start,0}};var q=new PriorityQueue<int,int>();q.Enqueue(start,0);while(q.Count>0){int cur=q.Dequeue();if(cur==destination)break;foreach(var p in _portals.Where(x=>x.SourceMapId==cur)){int d=dist[cur]+1;if(!dist.TryGetValue(p.DestinationMapId,out var old)||d<old){dist[p.DestinationMapId]=d;prev[p.DestinationMapId]=p;q.Enqueue(p.DestinationMapId,d);}}}if(!prev.ContainsKey(destination))return null;var route=new List<MapPortal>();for(int cur=destination;cur!=start;){if(!prev.TryGetValue(cur,out var p))return null;route.Add(p);cur=p.SourceMapId;}route.Reverse();var plan=new List<MultiMapTransitLeg>(route.Count);for(int i=0;i<route.Count;i++)plan.Add(new(i+1,route[i].SourceMapId,_mapNames.GetValueOrDefault(route[i].SourceMapId,$"Map_{route[i].SourceMapId}"),route[i].SourcePosition,route[i]));return plan;}
}

public sealed class NavigationExecutionController
{
    private readonly AStarPathfinder _pathfinder=new();private readonly PathSmoother _smoother=new();private ImmutableArray<GridPoint> _activePath=ImmutableArray<GridPoint>.Empty;private int _currentWaypointIndex;private GridPoint _lastObservedPosition;private int _stationaryTicks;private NavigationStatus _status=NavigationStatus.Idle;
    public NavigationStatus Status=>_status;public ImmutableArray<GridPoint> ActivePath=>_activePath;public GridPoint? CurrentWaypoint=>_currentWaypointIndex<_activePath.Length?_activePath[_currentWaypointIndex]:null;
    public bool StartNavigation(MapGridData map,GridPoint start,GridPoint destination,bool applySmoothing=true){var result=_pathfinder.FindPath(map,start,destination);if(!result.IsPathFound){_status=NavigationStatus.PathNotFound;_activePath=ImmutableArray<GridPoint>.Empty;return false;}_activePath=applySmoothing?_smoother.SmoothPath(map,result.Waypoints):result.Waypoints;_currentWaypointIndex=0;_lastObservedPosition=start;_stationaryTicks=0;_status=NavigationStatus.Navigating;return true;}
    public GridPoint? UpdateNavigationTick(GridPoint actual,MapGridData map,GridPoint destination){if(_status!=NavigationStatus.Navigating||_currentWaypointIndex>=_activePath.Length)return null;if(actual==_lastObservedPosition){if(++_stationaryTicks>=4){_status=NavigationStatus.StuckDetectedRerouting;if(!StartNavigation(map,actual,destination,false))return null;return CurrentWaypoint;}}else{_stationaryTicks=0;_lastObservedPosition=actual;}if(actual.DistanceTo(_activePath[_currentWaypointIndex])<=1.5){_currentWaypointIndex++;if(_currentWaypointIndex>=_activePath.Length){_status=NavigationStatus.DestinationReached;return null;}}return _activePath[_currentWaypointIndex];}
    public void CancelNavigation(){_status=NavigationStatus.Idle;_activePath=ImmutableArray<GridPoint>.Empty;_currentWaypointIndex=0;_stationaryTicks=0;}
}
