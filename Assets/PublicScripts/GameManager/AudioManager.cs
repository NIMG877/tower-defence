using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Codice.CM.Client.Differences.Merge;
using System;
public class AudioManager
{
    public static AudioManager Manager
    {
        get
        {
            if (_instance == null)
            {
                _instance = new AudioManager();
            }
            return _instance;
        }
    }
    private static AudioManager _instance;
    private AudioSource _musicSource;
    private AudioSource _effectSource;
    private AudioSource _cameraAudioSource;
    private Dictionary<string, AudioClip> _generalAudioClipDic;
    private AudioManager()
    {
        GameObject cam = GameObject.Find("MCam");
        if (cam.TryGetComponent(out AudioSource cameraAS))
        {
            _cameraAudioSource = cameraAS;
        }
        else
        {
            _cameraAudioSource = cam.AddComponent<AudioSource>();
        }
        _generalAudioClipDic = new Dictionary<string, AudioClip>();
        foreach (var item in GameDataService.AudioPaths)
        {
            AddAudioToAudioPaths(item.Key, item.Value);
        }
    }
    public AudioClip AddAudioToAudioPaths(string audioName, string audioPath)
    {
        AudioClip clip = Resources.Load<AudioClip>(audioPath);
        _generalAudioClipDic[audioName] = clip;
        return clip;
    }
    public void PlayAudio(string clipName, int clipTimes, bool isMusic, bool breakLastAudio, string clipPath = null, GameObject playTarget = null)
    {
        AudioClip audioClip;
        if (_generalAudioClipDic.ContainsKey(clipName))
        {
            audioClip = _generalAudioClipDic[clipName];
        }
        else if (clipPath != null)
        {
            audioClip = AddAudioToAudioPaths(clipName, clipPath);
        }
        else
        {
            Debug.LogError("audio undefined");
            return;
        }
        AudioSource audioSource = isMusic ? _musicSource : _effectSource;
        if (playTarget == null)
        {
            audioSource = _cameraAudioSource;
        }
        else if (playTarget.TryGetComponent(out AudioSource targetAS))
        {
            audioSource = targetAS;
        }
        else
        {
            audioSource = playTarget.AddComponent<AudioSource>();
        }
        audioSource.PlayOneShot(audioClip);

    }
    public void PlayAudios((string name, int times)[] clipMessages, bool isMusic, bool breakLastAudio, string[] clipPaths = null, GameObject playTarget = null)
    {
        for (int i = 0; i < clipMessages.Length; i++)
        {
            PlayAudio(clipMessages[i].name, clipMessages[i].times, isMusic, breakLastAudio, clipPaths[i], playTarget);
        }

    }
}
