using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RayTracingMaster : MonoBehaviour
{
    public ComputeShader RayTracingShader;

    private RenderTexture _target;

    private Camera _camera;

    private uint _currentSample = 0;

    private Material _addMaterial;

    public Texture skybox;

    public Light DirectionalLight;

    private RenderTexture _converged;

    public int SphereSeed;

    private static bool _meshObjectsNeedRebuilding = false;

    private static List<RayTracingObject> _rayTracingObjects = new List<RayTracingObject>();
    
    public float _IOR = 2.83f;
    public float _AbsorbIntensity = 2.89f;
    public float _ColorAdd = 0.05f;
    public float _ColorMultiply = 1.96f;
    public float _Specular = 0.472f;
    struct Sphere
    {
        public Vector3 position;
        public float radius;
        public Vector3 albedo;
        public Vector3 specular;
        public Vector3 emission;
        public float smoothness;
    }

    struct MeshObject
    {
        public Matrix4x4 localToworldMatrix;
        public int indices_offset;
        public int indices_count;
        public Vector3 albedo;
        public Vector3 specular;
        public Vector3 emission;
        public float smoothness;
    }

    private static List<MeshObject> _meshObjects = new List<MeshObject>();

    private static List<Vector3> _vertices = new List<Vector3>();

    private static List<int> _indices = new List<int>();

    private ComputeBuffer _meshObjectBuffer;

    private ComputeBuffer _vertexBuffer;

    private ComputeBuffer _indexBuffer;

    public Vector2 SphereRadius = new Vector2(5.0f, 30.0f);
    public uint SphereMax = 10000;
    public float SpherePlacementRadius = 100.0f;
    private ComputeBuffer _sphereBuffer;

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        RebuildMeshObjectBuffers();
        Render(destination);
    }

    private void OnEnable()
    {
        _currentSample = 0;
        SetUpScene();
    }

    private void OnDisable()
    {
        if(_sphereBuffer != null)
        {
            _sphereBuffer.Release();
            _meshObjectBuffer.Release();
            _vertexBuffer.Release();
            _indexBuffer.Release();
        }
    }

    private void SetUpScene()
    {
        List<Sphere> spheres = new List<Sphere>();
        Random.InitState(SphereSeed);
        // Add a number of random spheres
        for (int i = 0; i < SphereMax; i++)
        {
            Sphere sphere = new Sphere();
            sphere.radius = SphereRadius.x + Random.value * (SphereRadius.y - SphereRadius.x);
            Vector2 randomPos = Random.insideUnitCircle * SpherePlacementRadius;
            sphere.position = new Vector3(randomPos.x, sphere.radius, randomPos.y);

            foreach (Sphere other in spheres)
            {
                float minDist = sphere.radius + other.radius;
                if (Vector3.SqrMagnitude(sphere.position - other.position) < minDist * minDist)
                    goto SkipSphere;
            }

            Color color = Random.ColorHSV();
            bool metal = Random.value < 0.5f;
            sphere.albedo = metal ? Vector3.zero : new Vector3(color.r, color.g, color.b);

            sphere.specular = metal ? new Vector3(color.r, color.g, color.b) : Vector3.one * 0.06f;

            sphere.smoothness = metal ? Random.value +0.5f : 0.1f;

            sphere.emission = metal ? new Vector3(color.r, color.g, color.b) * 0.5f : Vector3.one * (Random.value / 5);

            spheres.Add(sphere);
            SkipSphere:
                continue;
        }
        _sphereBuffer = new ComputeBuffer(spheres.Count, 56);
        _sphereBuffer.SetData(spheres);
    }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void Update()
    {
        if(transform.hasChanged)
        {
            _currentSample = 0;
            transform.hasChanged = false;
        }
    }

    private void SetShaderParams()
    {
        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        RayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);
        RayTracingShader.SetFloat("_Seed", Random.value);
        RayTracingShader.SetFloat("_IOR", _IOR);

        RayTracingShader.SetFloat("_ColorAdd", _ColorAdd);
        RayTracingShader.SetFloat("_AbsorbIntensity", _AbsorbIntensity);
        RayTracingShader.SetFloat("_ColorMultiply", _ColorMultiply);
        RayTracingShader.SetFloat("_Specular", _Specular);


        //RayTracingShader.SetInt("alpha", alpha);
        Vector3 l = DirectionalLight.transform.forward;
        RayTracingShader.SetVector("_DirectionalLight", new Vector4(l.x, l.y, l.z, DirectionalLight.intensity));
        
        SetComputeBuffer("_Spheres", _sphereBuffer);
        SetComputeBuffer("_MeshObjects",_meshObjectBuffer);
        SetComputeBuffer("_Vertices", _vertexBuffer);
        SetComputeBuffer("_Indices", _indexBuffer);

    }

    private void Render(RenderTexture destination)
    {
        InitRenderTexture();
        SetShaderParams();

        RayTracingShader.SetTexture(0, "_SkyboxTexture", skybox);

        RayTracingShader.SetTexture(0, "Result",_target);
        RayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));
        int threadGroupX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupY = Mathf.CeilToInt(Screen.height / 8.0f);
        RayTracingShader.Dispatch(0, threadGroupX, threadGroupY, 1);

        if (_addMaterial == null)
        {
            _addMaterial = new Material(Shader.Find("Unlit/AddShader"));
        }
        _addMaterial.SetFloat("_Sample", _currentSample);

        Graphics.Blit(_target, _converged, _addMaterial);
        Graphics.Blit(_converged, destination);

        _currentSample++;
    }

    private void InitRenderTexture()
    {
        if(_target == null || _target.width != Screen.width || _target.height != Screen.height)
        {
            if(_target != null)
            {
                _target.Release();
            }
            _target = new RenderTexture(Screen.width, Screen.height, 0,RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            _target.enableRandomWrite = true;
            _target.Create();
        }

        if (_converged == null || _converged.width != Screen.width || _converged.height != Screen.height)
        {
            if (_converged != null)
            {
                _converged.Release();
            }
            _converged = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            _converged.enableRandomWrite = true;
            _converged.Create();
        }
    }

    private static void CreateComputeBuffer<T>(ref ComputeBuffer buffer,List<T> data,int stride) where T:struct
    {
        if(buffer != null)
        {
            if(data.Count == 0 || buffer.count != data.Count || buffer.stride != stride)
            {
                buffer.Release();
                buffer = null;
            }
        }

        if(data.Count != 0 )
        {
            if (buffer == null)
            {
                buffer = new ComputeBuffer(data.Count, stride);
            }
        }

        buffer.SetData(data);
    }

    private void SetComputeBuffer(string name,ComputeBuffer buffer)
    {
        if(buffer != null)
        {
            RayTracingShader.SetBuffer(0, name, buffer);
        }
    }

    public static void RegisterObject(RayTracingObject obj)
    {
        _rayTracingObjects.Add(obj);
        _meshObjectsNeedRebuilding = true;
    }

    public static void UnregisterObject(RayTracingObject obj)
    {
        _rayTracingObjects.Remove(obj);
        _meshObjectsNeedRebuilding = false;
    }

    public void RebuildMeshObjectBuffers()
    {
        if(!_meshObjectsNeedRebuilding)
        {
            return;
        }

        _meshObjectsNeedRebuilding = false;
        _currentSample = 0;

        _meshObjects.Clear();
        _vertices.Clear();
        _indices.Clear();

        foreach(RayTracingObject obj in _rayTracingObjects)
        {
            Mesh mesh = obj.GetComponent<MeshFilter>().sharedMesh;

            int firstVertex = _vertices.Count;
            _vertices.AddRange(mesh.vertices);

            int firstIndex = _indices.Count;
            var indices = mesh.GetIndices(0);
            _indices.AddRange(indices.Select(index => index + firstVertex));

            _meshObjects.Add(new MeshObject()
            {
                localToworldMatrix = obj.transform.localToWorldMatrix,
                indices_offset = firstIndex,
                indices_count = indices.Length,
                albedo = new Vector3(obj.albedo.r, obj.albedo.g, obj.albedo.b),
                specular = new Vector3(obj.specular.r, obj.specular.g, obj.specular.b),
                emission = new Vector3(obj.emission.r, obj.emission.g, obj.emission.b),
                smoothness = obj.smothness
            }) ;
        }
        CreateComputeBuffer(ref _meshObjectBuffer, _meshObjects, 112);
        CreateComputeBuffer(ref _vertexBuffer, _vertices, 12);
        CreateComputeBuffer(ref _indexBuffer, _indices, 4);
    }
}
