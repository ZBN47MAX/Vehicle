using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace DelayNoMore.LightPath
{
    /** @brief 绘制选中模型外轮廓及其沿真实光源产生的二维轮廓，不创建玩法对象。 */
    public sealed class ProjectionSelectionOutline : System.IDisposable
    {
        /** @brief 共享边的面朝向统计。 */
        private struct Edge { public int A, B, Count, Front; }
        /** @brief 两个世界的描边网格。 */
        private readonly Mesh _model = new Mesh { name="Selection 3D", indexFormat=IndexFormat.UInt32 }, _screen = new Mesh { name="Selection 2D", indexFormat=IndexFormat.UInt32 };
        /** @brief 几何缓存签名。 */
        private int _signature;
        /** @brief 焊接UV接缝顶点后取得面向观察点的轮廓边，输出按端点对排列。 */
        public static List<Vector3> Silhouette(Vector3[] vertices, int[] triangles, Matrix4x4 matrix, Vector3 eye)
        {
            var positions = new List<Vector3>(); var weld = new Dictionary<Vector3,int>(); var map = new int[vertices.Length];
            for (int i=0; i<vertices.Length; i++)
            {
                if (!weld.TryGetValue(vertices[i], out int index)) { index=positions.Count; weld.Add(vertices[i],index); positions.Add(matrix.MultiplyPoint3x4(vertices[i])); }
                map[i]=index;
            }
            var edges = new Dictionary<(int,int),Edge>();
            for (int i=0; i+2<triangles.Length; i+=3)
            {
                int a=map[triangles[i]], b=map[triangles[i+1]], c=map[triangles[i+2]];
                bool front=Vector3.Dot(Vector3.Cross(positions[b]-positions[a],positions[c]-positions[a]),eye-positions[a])>0;
                AddEdge(edges,a,b,front); AddEdge(edges,b,c,front); AddEdge(edges,c,a,front);
            }
            var result=new List<Vector3>();
            foreach (var edge in edges.Values)
                if (edge.Count==1 || (edge.Front>0 && edge.Front<edge.Count)) { result.Add(positions[edge.A]); result.Add(positions[edge.B]); }
            return result;
        }
        /** @brief 登记共享边与两侧面朝向。 */
        private static void AddEdge(Dictionary<(int,int),Edge> edges,int a,int b,bool front)
        {
            var key=(Mathf.Min(a,b),Mathf.Max(a,b)); edges.TryGetValue(key,out Edge edge);
            edge.A=a; edge.B=b; edge.Count++; if(front) edge.Front++; edges[key]=edge;
        }
        /** @brief 同时向演示相机和二维捕获相机提交选中轮廓。 */
        public void Draw(ProjectionSource source,ProjectionSurface surface,Matrix4x4 mapping,Camera view,Camera capture,LightStageConfig config,Material material)
        {
            if(!source || !source.isActiveAndEnabled || !material) return;
            var filters=source.GetComponentsInChildren<MeshFilter>();
            int signature=source.GetHashCode() ^ mapping.GetHashCode() ^ view.transform.position.GetHashCode() ^ view.transform.rotation.GetHashCode() ^ surface.ProjectionOrigin.GetHashCode() ^ config.SelectionColor.GetHashCode() ^ config.SelectionWidth.GetHashCode();
            foreach(var filter in filters) signature=unchecked(signature*31+filter.transform.localToWorldMatrix.GetHashCode()+(filter.sharedMesh ? filter.sharedMesh.GetHashCode() : 0));
            if(_signature!=signature)
            {
                Build(_model,filters,surface,mapping,view,config,false);
                Build(_screen,filters,surface,mapping,view,config,true); _signature=signature;
            }
            Graphics.DrawMesh(_model,Matrix4x4.identity,material,config.DisplayLayer,view,0,null,ShadowCastingMode.Off,false);
            Graphics.DrawMesh(_screen,Matrix4x4.identity,material,surface.gameObject.layer,capture,0,null,ShadowCastingMode.Off,false);
        }
        /** @brief 构建面向相机或光源的轮廓；导入网格须允许Read/Write。 */
        private static void Build(Mesh target,MeshFilter[] filters,ProjectionSurface surface,Matrix4x4 mapping,Camera camera,LightStageConfig config,bool projected)
        {
            var vertices=new List<Vector3>(); var triangles=new List<int>(); var colors=new List<Color>();
            foreach(var filter in filters)
            {
                Mesh mesh=filter.sharedMesh; if(!mesh || !mesh.isReadable) continue;
                Matrix4x4 matrix=projected ? filter.transform.localToWorldMatrix : mapping*filter.transform.localToWorldMatrix;
                var edges=Silhouette(mesh.vertices,mesh.triangles,matrix,projected ? surface.ProjectionOrigin : camera.transform.position);
                for(int i=0;i<edges.Count;i+=2)
                {
                    Vector3 a=edges[i],b=edges[i+1]; float width=config.SelectionWidth;
                    if(projected)
                    {
                        Vector2 pa=surface.ProjectPoint(a),pb=surface.ProjectPoint(b);
                        if(!Clip(surface.Bounds,ref pa,ref pb)) continue;
                        a=new Vector3(pa.x,pa.y,surface.transform.position.z-.04f); b=new Vector3(pb.x,pb.y,surface.transform.position.z-.04f);
                        width/=Mathf.Max(.001f,mapping.MultiplyVector(Vector3.right).magnitude);
                    }
                    else { a+=(camera.transform.position-a).normalized*width*.3f; b+=(camera.transform.position-b).normalized*width*.3f; }
                    Vector3 side=Vector3.Cross(b-a,projected ? Vector3.forward : camera.transform.forward).normalized*width/2;
                    int start=vertices.Count; vertices.AddRange(new[]{a-side,b-side,b+side,a+side});
                    for(int j=0;j<4;j++) colors.Add(config.SelectionColor);
                    triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
                }
            }
            target.Clear(); target.SetVertices(vertices); target.SetColors(colors); target.SetTriangles(triangles,0); target.RecalculateBounds();
        }
        /** @brief 将二维描边裁剪到房间矩形。 */
        public static bool Clip(Rect bounds,ref Vector2 a,ref Vector2 b)
        {
            Vector2 d=b-a; float lo=0,hi=1;
            if(!Limit(-d.x,a.x-bounds.xMin,ref lo,ref hi)||!Limit(d.x,bounds.xMax-a.x,ref lo,ref hi)
                ||!Limit(-d.y,a.y-bounds.yMin,ref lo,ref hi)||!Limit(d.y,bounds.yMax-a.y,ref lo,ref hi)) return false;
            b=a+d*hi; a+=d*lo; return true;
        }
        /** @brief 裁剪单个半平面。 */
        private static bool Limit(float p,float q,ref float lo,ref float hi)
        {
            if(Mathf.Abs(p)<.000001f) return q>=0;
            float t=q/p; if(p<0) lo=Mathf.Max(lo,t); else hi=Mathf.Min(hi,t); return lo<=hi;
        }
        /** @brief 释放仅属于当前运行的网格。 */
        public void Dispose() { Object.Destroy(_model); Object.Destroy(_screen); }
    }
}
