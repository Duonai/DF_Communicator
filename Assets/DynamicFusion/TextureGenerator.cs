using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.IO;
using System.Diagnostics;

public class TextureGenerator : MonoBehaviour
{
    public bool selfRender = true;
    public bool useTransmitter = true;
    System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
    private bool isConnected = false;

    const int TEX_WIDTH = 512;
    const int TEX_HEIGHT = 424;

    public string textureMappedFileName = "my-shared-memory-m";
    public float TotalKBytes = TEX_WIDTH * TEX_HEIGHT * 3 / 1024;

    public Texture2D t2d;
    public bool updateTex = true;

    Color[] m_colors = new Color[TEX_WIDTH * TEX_HEIGHT];

    MemoryMappedFile mmf_tex;
    MemoryMappedViewStream mmfvs_tex;
    bool isMapped_tex = true;

    byte[] bTex = new byte[TEX_WIDTH * TEX_HEIGHT * 3];

    Shader standardShader;

    // Start is called before the first frame update
    void Start()
    {
        t2d = new Texture2D(TEX_WIDTH, TEX_HEIGHT, TextureFormat.RGB24, false);
        GetComponent<MeshRenderer>().material.mainTexture = t2d;
        GetComponent<MeshRenderer>().material.SetTextureScale("_MainTex", new Vector2(1, -1));
        GetComponent<MeshRenderer>().material.SetFloat("_Glossiness", .0f);
        standardShader = Shader.Find("Custom/Background");
        GetComponent<MeshRenderer>().material.shader = standardShader;
        MemoryMapping();
    }

    void MemoryMapping()
    {
        isMapped_tex = true;
        try
        {
            mmf_tex = MemoryMappedFile.OpenExisting(textureMappedFileName);
            mmfvs_tex = mmf_tex.CreateViewStream();
        }
        catch
        {
            isMapped_tex = false;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (updateTex)
        {
            if (isMapped_tex)
            {
                // read texture
                mmfvs_tex.Read(bTex, 0, TEX_WIDTH * TEX_HEIGHT * 3);
                mmfvs_tex.Position = 0;

                // render texture (if it needed)
                if (selfRender)
                {
                    t2d.LoadRawTextureData(bTex);
                    t2d.Apply();
                }

                // sending texture to client
                if (useTransmitter)
                    sendTexture();
            }
            else
                MemoryMapping();
        }
    }

    void sendTexture()
    {
        // it should be called after the connection is done
        if (gameObject.GetComponent<ServerScript>().isConnected)
            gameObject.GetComponent<ServerScript>().sendData(bTex, (ushort)0);
    }
}