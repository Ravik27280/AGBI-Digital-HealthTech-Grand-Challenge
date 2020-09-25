﻿/**
* Copyright 2015 IBM Corp. All Rights Reserved.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
*      http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*
*/
#pragma warning disable 0649
using UnityEngine;
using System.Collections;
using IBM.Watson.DeveloperCloud.Logging;
using IBM.Watson.DeveloperCloud.Services.SpeechToText.v1;
using IBM.Watson.DeveloperCloud.Utilities;
using IBM.Watson.DeveloperCloud.DataTypes;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.Animations;

public class ExampleStreaming : MonoBehaviour
{
    #region PLEASE SET THESE VARIABLES IN THE INSPECTOR
    [Space(10)]
    [Tooltip("The service URL (optional). This defaults to \"https://stream.watsonplatform.net/speech-to-text/api\"")]
    [SerializeField]
    private string _serviceUrl;
    [Tooltip("Text field to display the results of streaming.")]
    public Text ResultsField;
    [Header("IAM Authentication")]
    [Tooltip("The IAM apikey.")]
    [SerializeField]
    private string _iamApikey;

    string State;  // State if it is final or interim.

    [Header("Parameters")]
    // https://www.ibm.com/watson/developercloud/speech-to-text/api/v1/curl.html?curl#get-model
    [Tooltip("The Model to use. This defaults to en-US_BroadbandModel")]
    [SerializeField]
    private string _recognizeModel;
    #endregion


    private int _recordingRoutine = 0;
    private string _microphoneID = null;
    private AudioClip _recording = null;
    private int _recordingBufferSize = 1;
    private int _recordingHZ = 22050;

    // Our 3D Game objects
    public GameObject skull;
    public GameObject heart;
    
   

   
     



    [Header("Ironman Components")]
    public GameObject[] ironObjects = new GameObject[38];

    public float speed = 10; // Rotation Speed

    private SpeechToText _service;

    void Start()
    {
        LogSystem.InstallDefaultReactors();
        Runnable.Run(CreateService());

        // Set them all to inactive.
        skull.gameObject.SetActive(false);
        heart.gameObject.SetActive(false);
       




    }


    private IEnumerator CreateService()
    {
        if (string.IsNullOrEmpty(_iamApikey))
        {
            throw new WatsonException("Plesae provide IAM ApiKey for the service.");
        }

        //  Create credential and instantiate service
        Credentials credentials = null;

        //  Authenticate using iamApikey
        TokenOptions tokenOptions = new TokenOptions()
        {
            IamApiKey = _iamApikey
        };

        credentials = new Credentials(tokenOptions, _serviceUrl);

        //  Wait for tokendata
        while (!credentials.HasIamTokenData())
            yield return null;

        _service = new SpeechToText(credentials);
        _service.StreamMultipart = true;

        Active = true;
        StartRecording();
    }

    public bool Active
    {
        get { return _service.IsListening; }
        set
        {
            if (value && !_service.IsListening)
            {
                _service.RecognizeModel = (string.IsNullOrEmpty(_recognizeModel) ? "en-US_BroadbandModel" : _recognizeModel);
                _service.DetectSilence = true;
                _service.EnableWordConfidence = true;
                _service.EnableTimestamps = true;
                _service.SilenceThreshold = 0.01f;
                _service.MaxAlternatives = 0;
                _service.EnableInterimResults = true;
                _service.OnError = OnError;
                _service.InactivityTimeout = -1;
                _service.ProfanityFilter = false;
                _service.SmartFormatting = true;
                _service.SpeakerLabels = false;
                _service.WordAlternativesThreshold = null;
                _service.StartListening(OnRecognize, OnRecognizeSpeaker);
            }
            else if (!value && _service.IsListening)
            {
                _service.StopListening();
            }
        }
    }
    private void StartRecording()
    {
        if (_recordingRoutine == 0)
        {
            UnityObjectUtil.StartDestroyQueue();
            _recordingRoutine = Runnable.Run(RecordingHandler());
        }
    }
    private void StopRecording()
    {
        if (_recordingRoutine != 0)
        {
            Microphone.End(_microphoneID);
            Runnable.Stop(_recordingRoutine);
            _recordingRoutine = 0;
        }
    }

    private void OnError(string error)
    {
        Active = false;

        Log.Debug("ExampleStreaming.OnError()", "Error! {0}", error);
    }

    private IEnumerator RecordingHandler()
    {
        Log.Debug("ExampleStreaming.RecordingHandler()", "devices: {0}", Microphone.devices);
        _recording = Microphone.Start(_microphoneID, true, _recordingBufferSize, _recordingHZ);
        yield return null;      // let _recordingRoutine get set..

        if (_recording == null)
        {
            StopRecording();
            yield break;
        }

        bool bFirstBlock = true;
        int midPoint = _recording.samples / 2;
        float[] samples = null;

        while (_recordingRoutine != 0 && _recording != null)
        {
            int writePos = Microphone.GetPosition(_microphoneID);
            if (writePos > _recording.samples || !Microphone.IsRecording(_microphoneID))
            {
                Log.Error("ExampleStreaming.RecordingHandler()", "Microphone disconnected.");

                StopRecording();
                yield break;
            }

            if ((bFirstBlock && writePos >= midPoint)
              || (!bFirstBlock && writePos < midPoint))
            {
                // front block is recorded, make a RecordClip and pass it onto our callback.
                samples = new float[midPoint];
                _recording.GetData(samples, bFirstBlock ? 0 : midPoint);

                AudioData record = new AudioData();
                record.MaxLevel = Mathf.Max(Mathf.Abs(Mathf.Min(samples)), Mathf.Max(samples));
                record.Clip = AudioClip.Create("Recording", midPoint, _recording.channels, _recordingHZ, false);
                record.Clip.SetData(samples, 0);

                _service.OnListen(record);

                bFirstBlock = !bFirstBlock;
            }
            else
            {
                // calculate the number of samples remaining until we ready for a block of audio, 
                // and wait that amount of time it will take to record.
                int remaining = bFirstBlock ? (midPoint - writePos) : (_recording.samples - writePos);
                float timeRemaining = (float)remaining / (float)_recordingHZ;

                yield return new WaitForSeconds(timeRemaining);
            }

        }

        yield break;
    }

    private void OnRecognize(SpeechRecognitionEvent result, Dictionary<string, object> customData)
    {
        if (result != null && result.results.Length > 0)
        {
            foreach (var res in result.results)
            {
                foreach (var alt in res.alternatives) // This section outputs the Speech to Text Predicitons.
                {

                    State = res.final ? "Final" : "Interim";
                    string text = string.Format("{0} ({1}, {2:0.00})\n", alt.transcript, res.final ? "Final" : "Interim", alt.confidence);
                    Log.Debug("ExampleStreaming.OnRecognize()", text);


                    if (alt.transcript.Contains("hello") && State.Contains("Final")) // hello Vikram ! 
                    {
                        StopRecording();
                        StartCoroutine(ExampleTextToSpeech.I1_HelloRavi());       //When it detect these words it will execute a Coroutine in the Text to Speech script.
                        StartCoroutine(wait(4));
                                              
                       
                        
                       
                       
                        
                    }

                    if (text.Contains("problem") && State.Contains("Final")) // Bring up design interface
                    {
                        StopRecording();

                        StartCoroutine(ExampleTextToSpeech.I2_pro());
                        // Holodeck.gameObject.SetActive(true);                      //Activates game object/s on Command.

                        StartCoroutine(wait(4));
                    }

                    if (text.Contains("how") && State.Contains("Final")) // open up project edith prototype
                    {
                        StopRecording();
                        StartCoroutine(ExampleTextToSpeech.I3_How());
                        // Planet.gameObject.SetActive(true);
                        // Glasses.gameObject.SetActive(true);
                        StartCoroutine(wait(5));
                    }

                    if ((text.Contains("Ok")) && State.Contains("Final")) // pause rotation
                    {
                        StopRecording();
                        StartCoroutine(ExampleTextToSpeech.I4_ok());
                        //if (rotate.speed == 10)
                        //{
                        //    rotate.speed = 0;
                        //}
                        //else if (rotate.speed == 0)
                        //{
                        //    rotate.speed = 10;
                        //}
                        //StartCoroutine(wait(3));
                    }
                    if (text.Contains("heart") && State.Contains("Final")) // Jarvis bring up status
                    {
                        StopRecording();
                        StartCoroutine(ExampleTextToSpeech.I5_Heart());
                        StartCoroutine(wait(7));
                        heart.SetActive(true);
                    }

                    if (text.Contains("skull") && State.Contains("Final")) // Integrate deadshot module
                    {
                        StopRecording();
                        StartCoroutine(ExampleTextToSpeech.I6_Skull());
                        skull.SetActive(true);
                        
                        StartCoroutine(wait(5));
                    }

                    if (text.Contains("close") && State.Contains("Final"))
                    {
                        StopRecording();
                        StartCoroutine(ExampleTextToSpeech.I7_Close());
                        StartCoroutine(wait(4));
                        heart.SetActive(false);
                        skull.SetActive(false);

                    }

                    //if (text.Contains("bye") && State.Contains("Final")) // Jarvis activate edith protocol
                    //{
                        
                    //}

                    //if (text.Contains("close") && State.Contains("Final")) // yeah, seems good idea
                    //{
                    //    StopRecording();
                    //    StartCoroutine(ExampleTextToSpeech.I9_Ahead());
                    //    Planet.gameObject.SetActive(false);
                    //    StartCoroutine(wait(5));
                    //}

                    //if (text.Contains("thanks") && State.Contains("Final")) // thanks edith
                    //{
                    //    StopRecording();
                    //    StartCoroutine(ExampleTextToSpeech.I10_Thank());
                    //    StartCoroutine(wait(4));
                    //}

                    //if (text.Contains("some") && State.Contains("Final")) // hi edith could you suggest me some improvements on the deadshot module?
                    //{
                    //    StopRecording();
                    //    StartCoroutine(ExampleTextToSpeech.I11_Looking());
                    //    StartCoroutine(wait(7));
                    //}

                    //if (text.Contains("mark") && State.Contains("Final")) // Jarvis, I would like to open a new project file indexed as mark 2
                    //{
                    //    StopRecording();
                    //    StartCoroutine(ExampleTextToSpeech.I12_Mark());
                    //    Holodeck.gameObject.SetActive(false);
                    //    StartCoroutine(wait(4));
                    //}

                    //if (text.Contains("sure") && State.Contains("Final")) // Sure
                    //{
                    //    StopRecording();
                    //    StartCoroutine(ExampleTextToSpeech.I13_Sure());
                    //    StartCoroutine(wait(3));
                    //}

                    //if (text.Contains("all") && State.Contains("Final")) // Import all components from home interface
                    //{
                    //    StopRecording();
                    //    StartCoroutine(ExampleTextToSpeech.I14_Home());
                    //    StartCoroutine(wait(2));
                    //}

                    //if (text.Contains("right") && State.Contains("Final")) // Alright, What do you say?
                    //{
                    //    StopRecording();
                    //    StartCoroutine(ExampleTextToSpeech.I15_Alright());
                    //    StartCoroutine(wait(5));
                    //}

                    //if (text.Contains("start") && State.Contains("Final")) // Start the virtual walkaround
                    //{
                    //    StopRecording();
                    //    StartCoroutine(ExampleTextToSpeech.I16_Start());
                    //    Ironman.gameObject.SetActive(true);
                    //    StartCoroutine(wait(5));
                    //}

                    //if (text.Contains("ratio") && State.Contains("Final")) // Reconfigure shell metals and integrate all components while maintaining power to weight ratio
                    //{
                    //    StopRecording();
                    //    StartCoroutine(ExampleTextToSpeech.I17_metals());
                    //    StartCoroutine(wait(5));
                    //}

                    //if (text.Contains("okay") && State.Contains("Final")) // Yes
                    //{
                    //    StopRecording();
                    //    StartCoroutine(Render.MoveTargetToDestinations(ironObjects, 5));

                    //    StartCoroutine(ExampleTextToSpeech.I18_Complete());
                    //    StartCoroutine(ExampleTextToSpeech.I19_Notify());
                    //    StartCoroutine(wait(30));
                    //}

                    //if (text.Contains("man") && State.Contains("Final")) // Thanks man
                    //{
                    //    StopRecording();
                    //    StartCoroutine(ExampleTextToSpeech.I20_Enjoy());
                    //    StartCoroutine(wait(3));
                    //}

                    ResultsField.text = text;
                }

                if (res.keywords_result != null && res.keywords_result.keyword != null)
                {
                    foreach (var keyword in res.keywords_result.keyword)
                    {
                        Log.Debug("ExampleStreaming.OnRecognize()", "keyword: {0}, confidence: {1}, start time: {2}, end time: {3}", keyword.normalized_text, keyword.confidence, keyword.start_time, keyword.end_time);
                    }
                }

                if (res.word_alternatives != null)
                {
                    foreach (var wordAlternative in res.word_alternatives)
                    {
                        Log.Debug("ExampleStreaming.OnRecognize()", "Word alternatives found. Start time: {0} | EndTime: {1}", wordAlternative.start_time, wordAlternative.end_time);
                        foreach (var alternative in wordAlternative.alternatives)
                            Log.Debug("ExampleStreaming.OnRecognize()", "\t word: {0} | confidence: {1}", alternative.word, alternative.confidence);
                    }
                }
            }
        }
    }

    IEnumerator wait(int time)
    {
        yield return new WaitForSeconds(time);
        StartRecording();
    }

    private void OnRecognizeSpeaker(SpeakerRecognitionEvent result, Dictionary<string, object> customData)
    {
        if (result != null)
        {
            foreach (SpeakerLabelsResult labelResult in result.speaker_labels)
            {
                Log.Debug("ExampleStreaming.OnRecognize()", string.Format("speaker result: {0} | confidence: {3} | from: {1} | to: {2}", labelResult.speaker, labelResult.from, labelResult.to, labelResult.confidence));
            }
        }
    }
}