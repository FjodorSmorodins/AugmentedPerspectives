using UnityEngine;
using UnityEngine.Rendering;

public class AssemblySuccessFeedback : MonoBehaviour
{
    [SerializeField] private Transform effectOrigin;
    [SerializeField] private Material particleMaterial;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.35f;

    private AudioSource sound;
    private AudioClip chime;
    private ParticleSystem particles;
    private Mesh particleMesh;

    public void PlaySuccess()
    {
        if (sound == null)
        {
            var audioObject = new GameObject("Success Chime");
            audioObject.transform.SetParent(transform, false);
            sound = audioObject.AddComponent<AudioSource>();
            sound.playOnAwake = false;
            sound.spatialBlend = 0f;
            chime = CreateChime();
        }
        sound.PlayOneShot(chime, soundVolume);

        if (particles == null)
            CreateParticles();
        particles.transform.position = (effectOrigin != null ? effectOrigin.position : transform.position)
                                       + Vector3.up * 0.25f;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.Play();
        particles.Emit(24);
    }

    public void ResetFeedback()
    {
        if (sound != null)
            sound.Stop();
        if (particles != null)
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void CreateParticles()
    {
        var effect = new GameObject("Green Success Burst");
        effect.SetActive(false);
        effect.transform.SetParent(transform, false);
        particles = effect.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.03f);
        main.startColor = new Color(0.15f, 1f, 0.25f);
        main.gravityModifier = 0.15f;
        main.maxParticles = 32;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 30f;
        shape.radius = 0.12f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        particleMesh = new Mesh
        {
            name = "Success Particle",
            vertices = new[] { Vector3.up * 0.5f, Vector3.down * 0.5f, Vector3.left * 0.5f,
                               Vector3.right * 0.5f, Vector3.forward * 0.5f, Vector3.back * 0.5f },
            triangles = new[] { 0,2,4, 0,4,3, 0,3,5, 0,5,2, 1,4,2, 1,3,4, 1,5,3, 1,2,5 }
        };
        particleMesh.RecalculateNormals();
        particleMesh.RecalculateBounds();
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = particleMesh;
        renderer.sharedMaterial = particleMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        effect.SetActive(true);
    }

    private static AudioClip CreateChime()
    {
        const int sampleRate = 44100;
        const float duration = 0.28f;
        var samples = new float[Mathf.CeilToInt(sampleRate * duration)];
        for (int i = 0; i < samples.Length; i++)
        {
            float time = (float)i / sampleRate;
            float first = Note(time, 659.25f, 0.18f);
            float second = Note(time - 0.09f, 987.77f, 0.19f);
            samples[i] = 0.25f * (first + second);
        }
        AudioClip clip = AudioClip.Create("Assembly Success Chime", samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static float Note(float time, float frequency, float duration)
    {
        if (time < 0f || time >= duration)
            return 0f;
        float envelope = Mathf.Min(time / 0.008f, 1f) * Mathf.Pow(1f - time / duration, 2f);
        return Mathf.Sin(2f * Mathf.PI * frequency * time) * envelope;
    }

    private void OnDestroy()
    {
        if (chime != null)
            Destroy(chime);
        if (particleMesh != null)
            Destroy(particleMesh);
    }
}
