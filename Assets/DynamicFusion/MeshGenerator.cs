using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Threading;

// to use stopwatch
using debug = UnityEngine.Debug;

public enum GeneratorType
{
    RelayMode,
    RenderMode,
    RenderAndRealyMode,
}

public enum TransferDataType
{
    TexturedVertex,
    TextureOnly,
    VertexOnly,
}
public enum ShaderType
{
    standarad, 
    unlit,
    normal,
}

public class MeshGenerator : MonoBehaviour
{
    // parameters
    public string vertexMappedFileName = "my-shared-memory-v";
    public string textureMappedFileName = "my-shared-memory-t";

    public GeneratorType G_Type;
    public TransferDataType T_Type;

    // public variables
    public int nVertices = 0;
    public int nIndices = 0;
    public float nTotalKBytes = 0;

    public bool recalNormal = false;
    public bool updateDF = true;
    public bool updateMesh = true;
    public bool updateTexture = true;
    public bool useDirectProcess = false;

    public bool reassignCollider = false; ///

    public Texture2D t2d;

    // Timer
    public float Time_Reading_v = 0.0f;
    public float Time_Reading_t = 0.0f;
    public float Time_Converiting_v = 0.0f;
    public float Time_Converiting_t = 0.0f;
    public float Time_Direct_Process = 0.0f;
    float collider_timer = 0.0f;

    // Constants
    const int MAX_VERTICES = 33000;
    const int MAX_INDICIES = 210000;

    //const int MAX_VERTICES = 150000;
    //const int MAX_INDICIES = 750000;

    const int nEntities = 5;
    const int TEX_WIDTH = 512;
    const int TEX_HEIGHT = 424;

    // Mesh information
    Vector3[] baseVertices  = new Vector3[MAX_VERTICES];
    Vector3[] baseNormals   = new Vector3[MAX_VERTICES];
    Vector2[] baseUVs       = new Vector2[MAX_VERTICES];
    int[] baseTriangleList  = new int[MAX_INDICIES];
    Shader unlitShader;
    Shader standardShader;
    Shader normalShader;
    ShaderType ST;

    // Memory map connection
    MemoryMappedFile mmf;
    MemoryMappedViewStream mmfvs;

    MemoryMappedFile mmf_tex;
    MemoryMappedViewStream mmfvs_tex;

    bool isMapped = true;
    bool isConnected = false;

    // Memory map slot
    byte[] bnVerticies          = new byte[4];
    byte[] bnIndices            = new byte[4];
    byte[] bData                = new byte[MAX_VERTICES * nEntities * 4 + MAX_INDICIES * 2];
    byte[] cData                = new byte[TEX_WIDTH * TEX_HEIGHT * 3 + MAX_VERTICES * nEntities * 4 + MAX_INDICIES * 2];
    byte[] bTex                 = new byte[TEX_WIDTH * TEX_HEIGHT * 3];
    float[] fData               = new float[MAX_VERTICES * nEntities * 4];
    ushort[] usTriangleIndex    = new ushort[MAX_INDICIES];

    int[] curTime = new int[1];

    Thread mmfThread;
    AutoResetEvent mmfFlag = new AutoResetEvent(false);

    // Threading
    bool runMmf;
    bool isMmfDone = false;

    // stop watch
    Stopwatch sw_reading_v = new Stopwatch();
    Stopwatch sw_reading_t = new Stopwatch();
    Stopwatch sw_converting_v = new Stopwatch();
    Stopwatch sw_converting_t = new Stopwatch();
    Stopwatch sw_total = new Stopwatch();

    // Start is called before the first frame update
    void Start()
    {
        // Mesh setting
        AllocateInitMesh();

        Mesh mesh = new Mesh();
        mesh.MarkDynamic();
        mesh.indexFormat    = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices       = baseVertices;
        mesh.triangles      = baseTriangleList;
        mesh.normals        = baseNormals;
        mesh.uv             = baseUVs;

        GetComponent<MeshFilter>().sharedMesh= mesh;
        GetComponent<MeshFilter>().sharedMesh.MarkDynamic();

        GetComponent<MeshCollider>().sharedMesh = mesh; ///

        t2d = new Texture2D(TEX_WIDTH, TEX_HEIGHT, TextureFormat.RGB24, false);
        GetComponent<MeshRenderer>().material.mainTexture = t2d;
        GetComponent<MeshRenderer>().material.SetTextureScale("_MainTex", new Vector2(1, -1));
        GetComponent<MeshRenderer>().material.SetFloat("_Glossiness", .0f);

        unlitShader = Shader.Find("Unlit/Texture");
        standardShader = Shader.Find("Custom/StencilWrite");
        normalShader = Shader.Find("Unlit/WorldSpaceNormals");
        GetComponent<MeshRenderer>().material.shader = standardShader;

        // set memory mapped file
        if (!useDirectProcess)
            MemoryMapping();
    }

    void AllocateInitMesh()
    {
        for (int cnt = 0; cnt < MAX_VERTICES; cnt++)
        {
            baseVertices[cnt] = new Vector3(0, 0, 0);
            baseNormals[cnt] = new Vector3(0, 1, 0);
            baseUVs[cnt] = new Vector2(1, 1);
            baseTriangleList[cnt] = cnt;
        }

        for (int cnt = MAX_VERTICES; cnt < MAX_INDICIES; cnt++)
        {
            baseTriangleList[cnt] = MAX_VERTICES -1;
        }
    }

    void DirectProcessing()
    {
        sw_total.Start();

        if (!isConnected)
        {
            try
            {
                mmf = MemoryMappedFile.OpenExisting(vertexMappedFileName);
                mmfvs = mmf.CreateViewStream();

                mmf_tex = MemoryMappedFile.OpenExisting(textureMappedFileName);
                mmfvs_tex = mmf_tex.CreateViewStream();

                isConnected = true;
            }
            catch(Exception e)
            {
                isConnected = false;
                UnityEngine.Debug.Log("Memory Map is not found (Check the DF framework is running)");
                return;
            }
        }

        // get vertex data from DF engine ------------------------------------------------------------
        sw_reading_v.Start();

        mmfvs.Read(bnVerticies, 0, 4);
        nVertices = BitConverter.ToInt32(bnVerticies, 0);

        mmfvs.Read(bnIndices, 0, 4);
        nIndices = BitConverter.ToInt32(bnIndices, 0);

        // return pointer to 0 and read whole data in one array
        mmfvs.Position = 0;
        mmfvs.Read(bData, 4, 8 + nVertices * nEntities * 4 + nIndices * 2);
        mmfvs.Position = 0;

        if (G_Type == GeneratorType.RenderMode || G_Type == GeneratorType.RenderAndRealyMode)
        {
            //block copy (casting data type)
            Buffer.BlockCopy(bData, 12, fData, 0, nVertices * nEntities * 4);
            Buffer.BlockCopy(bData, 12 + nVertices * nEntities * 4, usTriangleIndex, 0, nIndices * 2);
        }
        
        nTotalKBytes = 646 + (nIndices * 2 + nVertices * 4 * nEntities + 8) / 1024.0f;

        sw_reading_v.Stop();
        Time_Reading_v = (float)sw_reading_v.ElapsedMilliseconds;
        sw_reading_v.Reset();

        // get texture data from DF engine ------------------------------------------------------------
        sw_reading_t.Start();
        
        // read texture (fixed size)
        mmfvs_tex.Read(bTex, 0, TEX_WIDTH * TEX_HEIGHT * 3);
        mmfvs_tex.Position = 0;

        sw_reading_t.Stop();
        Time_Reading_t = (float)sw_reading_t.ElapsedMilliseconds;
        sw_reading_t.Reset();

        if(T_Type == TransferDataType.TextureOnly)
        {
            if (updateTexture)
            {
                t2d.LoadRawTextureData(bTex);
                t2d.Apply();
            }
            sendData();
            return;
        }

        
        if (isMapped && updateDF)
        {
            if (nVertices < MAX_VERTICES && nVertices > 0)
            {
                if (G_Type == GeneratorType.RelayMode || G_Type == GeneratorType.RenderAndRealyMode)
                    sendData();

                if ((G_Type == GeneratorType.RenderMode || G_Type == GeneratorType.RenderAndRealyMode) && T_Type == TransferDataType.TexturedVertex)
                {
                    sw_converting_v.Start();
                    if (updateMesh)
                    {
                        // mesh manipulation set up (set to use more than 65255 vertices)----------------------
                        Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
                        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                        Vector3[] vertices = mesh.vertices;
                        Vector2[] uvs = mesh.uv;
                        int[] triangles = mesh.triangles;

                        Array.Copy(baseVertices, vertices, nVertices);
                        Array.Copy(baseUVs, uvs, nVertices);
                        Array.Copy(baseTriangleList, triangles, MAX_INDICIES);

                        // serialized version
                        for (int cnt = 0; cnt < nVertices; cnt++)
                        {
                            //uvs[cnt].x = fData[cnt * nEntities + 0];
                            //uvs[cnt].y = fData[cnt * nEntities + 1];

                            //vertices[cnt].x = fData[cnt * nEntities + 2];
                            //vertices[cnt].y = fData[cnt * nEntities + 3];
                            //vertices[cnt].z = fData[cnt * nEntities + 4];

                            vertices[cnt].x = fData[cnt * nEntities + 0];
                            vertices[cnt].y = fData[cnt * nEntities + 1];
                            vertices[cnt].z = fData[cnt * nEntities + 2];
                           
                            uvs[cnt].x = fData[cnt * nEntities + 3];
                            uvs[cnt].y = fData[cnt * nEntities + 4];
                        }

                        for (int cnt = 0; cnt < nIndices; cnt++)
                        {
                            triangles[cnt] = (int)usTriangleIndex[cnt];
                        }

                        //re - asign mesh data
                        mesh.vertices = vertices;
                        mesh.uv = uvs;
                        mesh.triangles = triangles;

                        if(recalNormal)
                        {
                            Vector3[] normals = new Vector3[vertices.Length];
                            for (int cnt = 0; cnt < triangles.Length; cnt += 3)
                            {
                                Vector3 v0 = vertices[triangles[cnt]];
                                Vector3 v1 = vertices[triangles[cnt + 1]];
                                Vector3 v2 = vertices[triangles[cnt + 2]];

                                Vector3 normal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));

                                normals[triangles[cnt]] += normal;
                                normals[triangles[cnt + 1]] += normal;
                                normals[triangles[cnt + 2]] += normal;
                            }

                            for (int cnt = 0; cnt < vertices.Length; cnt++)
                            {
                                normals[cnt] = Vector3.Normalize(normals[cnt]);
                            }
                            mesh.normals = normals;
                        }
                        else
                            mesh.RecalculateNormals();

                        mesh.RecalculateBounds();
                    }

                    sw_converting_v.Stop();
                    Time_Converiting_v = (float)sw_converting_v.ElapsedMilliseconds;
                    sw_converting_v.Reset();

                    sw_converting_t.Start();
                    if (updateTexture)
                    {
                        t2d.LoadRawTextureData(bTex);
                        t2d.Apply();
                    }
                    sw_converting_t.Stop();
                    Time_Converiting_t = (float)sw_converting_t.ElapsedMilliseconds;
                    sw_converting_t.Reset();
                }

                if (G_Type != GeneratorType.RelayMode && G_Type != GeneratorType.RenderAndRealyMode && G_Type != GeneratorType.RenderMode)
                    UnityEngine.Debug.Log("Check the GeneratorType");
            }
            else if (nVertices > MAX_VERTICES)
                UnityEngine.Debug.Log("# of Vertices over the limits");

           
        }

        sw_total.Stop();
        Time_Direct_Process = (float)sw_total.ElapsedMilliseconds;
        sw_total.Reset();
    }

    void MemoryMapping()
    {
        isMapped = true;

        // Memory map setting
        try
        {
            sw_reading_v.Start();

            mmf = MemoryMappedFile.OpenExisting(vertexMappedFileName);
            mmfvs = mmf.CreateViewStream();

            mmfThread = new Thread(() =>
            {
                runMmf = true;

                while (runMmf)
                {
                    // get data from DF engine ------------------------------------------------------------
                    mmfvs.Read(bnVerticies, 0, 4);
                    nVertices = BitConverter.ToInt32(bnVerticies, 0);

                    mmfvs.Read(bnIndices, 0, 4);
                    nIndices = BitConverter.ToInt32(bnIndices, 0);

                    // return pointer to 0 ard read whole data in one array
                    mmfvs.Position = 0;
                    mmfvs.Read(bData, 0, 8 + nVertices * nEntities * 4 + nIndices * 2);

                    if (G_Type == GeneratorType.RenderMode || G_Type == GeneratorType.RenderAndRealyMode) 
                    {
                        //block copy (casting data type)
                        Buffer.BlockCopy(bData, 8, fData, 0, nVertices * nEntities * 4);
                        Buffer.BlockCopy(bData, 8 + nVertices * nEntities * 4, usTriangleIndex, 0, nIndices * 2);
                    }

                    // wait
                    isMmfDone = true;
                    nTotalKBytes = (nIndices * 2 + nVertices * 4 * nEntities + 8) / 1024.0f;
                    mmfFlag.WaitOne();
                }
            });
            mmfThread.Start();
            sw_reading_v.Stop();
        }
        catch
        {
            isMapped = false;
        }

        Time_Reading_v = (float)sw_reading_v.ElapsedMilliseconds;
        sw_reading_v.Reset();

    }

    void Processing()
    {
        // Mapping is done
        if (isMapped && updateDF)
        {
            if (isMmfDone && nVertices < MAX_VERTICES && nVertices > 0)
            {
                if (G_Type == GeneratorType.RelayMode || G_Type == GeneratorType.RenderAndRealyMode)
                    sendData();

                if (G_Type == GeneratorType.RenderMode || G_Type == GeneratorType.RenderAndRealyMode)
                {
                    if (updateMesh)
                    {
                        sw_converting_v.Start();

                        // mesh manipulation set up (set to use more than 65255 vertices)----------------------
                        Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
                        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                        Vector3[] vertices = mesh.vertices;
                        Vector2[] uvs = mesh.uv;
                        int[] triangles = mesh.triangles;

                        Array.Copy(baseVertices, vertices, nVertices);
                        Array.Copy(baseUVs, uvs, nVertices);
                        Array.Copy(baseTriangleList, triangles, MAX_INDICIES);

                        // serialized version
                        for (int cnt = 0; cnt < nVertices; cnt++)
                        {
                            //uvs[cnt].x = fData[cnt * nEntities + 0];
                            //uvs[cnt].y = fData[cnt * nEntities + 1];

                            //vertices[cnt].x = fData[cnt * nEntities + 2];
                            //vertices[cnt].y = fData[cnt * nEntities + 3];
                            //vertices[cnt].z = fData[cnt * nEntities + 4];

                            vertices[cnt].x = fData[cnt * nEntities + 0];
                            vertices[cnt].y = fData[cnt * nEntities + 1];
                            vertices[cnt].z = fData[cnt * nEntities + 2];

                            uvs[cnt].x = fData[cnt * nEntities + 3];
                            uvs[cnt].y = fData[cnt * nEntities + 4];
                        }

                        for (int cnt = 0; cnt < nIndices; cnt++)
                        {
                            triangles[cnt] = (int)usTriangleIndex[cnt];
                        }

                        //re - asign mesh data
                        mesh.vertices = vertices;
                        mesh.uv = uvs;
                        mesh.triangles = triangles;

                        mesh.RecalculateNormals();    ///
                        mesh.RecalculateBounds();     ///
                    }

                    if (reassignCollider && collider_timer > 4.0f)
                    {
                        GetComponent<MeshCollider>().sharedMesh = GetComponent<MeshFilter>().sharedMesh;
                        //reassignCollider = false;
                        //UnityEngine.Debug.Log("reassignColliders");
                        collider_timer = 0.0f;
                    }
                    collider_timer++;

                    sw_converting_v.Stop();
                    Time_Converiting_v = (float)sw_converting_v.ElapsedMilliseconds;
                    sw_converting_v.Reset();
                }

                if (G_Type != GeneratorType.RelayMode && G_Type != GeneratorType.RenderAndRealyMode && G_Type != GeneratorType.RenderMode)
                    UnityEngine.Debug.Log("Check the GeneratorType");
            }
            else if (nVertices > MAX_VERTICES)
                UnityEngine.Debug.Log("# of Vertices over the limits");

            // return read header to origin (for next frame)
            mmfvs.Position = 0;
            isMmfDone = false;
            mmfFlag.Set();
        }
        // retry to map the target
        else if (!isMapped)
        {
            MemoryMapping();
        }

    }

    // Update is called once per frame
    void Update()
    {
        if (useDirectProcess)
            DirectProcessing();
        else
            Processing();

        if(Input.GetKeyDown(KeyCode.Space))
        {
            if(ST == ShaderType.standarad)
            {
                ST = ShaderType.unlit;
                GetComponent<MeshRenderer>().material.shader = unlitShader;
            }
            else if(ST == ShaderType.unlit)
            {
                ST = ShaderType.normal;
                GetComponent<MeshRenderer>().material.shader = normalShader;
            }
            else if (ST == ShaderType.normal)
            {
                ST = ShaderType.standarad;
                GetComponent<MeshRenderer>().material.shader = standardShader;
            }
        }

        if(Input.GetKeyDown(KeyCode.A))
        {
            Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var triangles = mesh.triangles;

            Vector3[] vertex50 = new Vector3[10];
            int vertex_index = 0;

            for(int cnt = 0; cnt < triangles.Length; cnt++)
            {
                if (triangles[cnt] == 50)
                {
                    int triangleNumber = (cnt - (cnt % 3))/3;

                    vertex50[vertex_index] = new Vector3(triangles[triangleNumber * 3], triangles[triangleNumber * 3 + 1], triangles[triangleNumber * 3 + 2]);
                    vertex_index++;

                    if (vertex_index == 10)
                        UnityEngine.Debug.Log("Index is fool");
                }
            }

            UnityEngine.Debug.Log("Number of Triangles shares vertex 50: " + vertex_index.ToString());

            for(int cnt = 0; cnt < vertex_index; cnt++)
            {
                Vector3 normal_index = vertex50[cnt];
                UnityEngine.Debug.Log("Vertices in Triangle : " + vertex50[cnt].ToString());
                UnityEngine.Debug.Log("\t normal[" + ((int)normal_index.x).ToString() + "]" + normals[(int)normal_index.x].ToString()
                    + " / " +            "normal[" + ((int)normal_index.y).ToString() + "]" + normals[(int)normal_index.y].ToString() 
                    + " / " +            "normal[" + ((int)normal_index.z).ToString() + "]" + normals[(int)normal_index.z].ToString() + "\n");

            }


        }
    }

    void sendData()
    {
        // it should be called after the connection is done
        if (gameObject.GetComponent<ServerScript>().isConnected)
        {
            curTime[0] = DateTime.Now.Second * 1000 + DateTime.Now.Millisecond;

            if(T_Type == TransferDataType.TexturedVertex)
            {
                Buffer.BlockCopy(curTime, 0, cData, 0, 4);
                Buffer.BlockCopy(bData, 4, cData, 4, 8 + (nVertices * nEntities * 4) + (nIndices * 2));
                Buffer.BlockCopy(bTex, 0, cData, 12 + (nVertices * nEntities * 4 + nIndices * 2), (TEX_WIDTH * TEX_HEIGHT * 3));

                // type 0 = vertex + texture
                gameObject.GetComponent<ServerScript>().sendData(cData, 12 + nVertices * nEntities * 4 + nIndices * 2 + TEX_WIDTH * TEX_HEIGHT * 3, (ushort)0);
            }
            else if(T_Type == TransferDataType.TextureOnly)
            {
                gameObject.GetComponent<ServerScript>().sendData(bTex, TEX_WIDTH * TEX_HEIGHT * 3, (ushort)1);
 
            }
            else if(T_Type == TransferDataType.VertexOnly)
            {
                Buffer.BlockCopy(curTime, 0, bData, 0, 4);
                gameObject.GetComponent<ServerScript>().sendData(bData, (12 + nVertices * 4 * nEntities + nIndices * 2), (ushort)2);
            }
            else
            {  
                UnityEngine.Debug.Log("Not at all");
            }
        }
    }

    //@CAUTION:: if these functions are uncommented Unity main framework will be stucked
    private void OnDestroy()
    {
        //runMmf = false;
        //mmfFlag.Set();
        //mmfThread.Join();
    }
}
