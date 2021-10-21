using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor.Recorder.AOV;
using UnityEngine;
using UnityEngine.Profiling;

namespace UnityEditor.Recorder
{
    class AOVRecorder : BaseTextureRecorder<AOVRecorderSettings>
    {
        public bool m_asyncShaderCompileSetting;
        Queue<string> m_PathQueue = new Queue<string>();
        protected override TextureFormat ReadbackTextureFormat
        {
            get
            {
                return Settings.m_OutputFormat != ImageRecorderSettings.ImageRecorderOutputFormat.EXR
                    ? TextureFormat.RGBA32
                    : TextureFormat.RGBAFloat;
            }
        }

        protected internal override bool BeginRecording(RecordingSession session)
        {
#if !HDRP_AVAILABLE
            // This can happen with an AOV Recorder Clip in a project after removing HDRP
            return settings.HasErrors();
#else
            if (!base.BeginRecording(session))
            {
                return false;
            }

            if (settings.HasErrors())
            {
                Debug.LogError($"The '{settings.name}' AOV Recorder has errors and cannot record any data.");
                return false;
            }

            // Save the async compile shader setting to restore it at the end of recording
            m_asyncShaderCompileSetting = EditorSettings.asyncShaderCompilation;
            // Disable async compile shader setting when recording
            EditorSettings.asyncShaderCompilation = false;

            Settings.FileNameGenerator.CreateDirectory(session);
            return true;
#endif
        }

        protected internal override void EndRecording(RecordingSession session)
        {
            // Restore the asyncShaderCompilation setting
            EditorSettings.asyncShaderCompilation = m_asyncShaderCompileSetting;
            base.EndRecording(session);
        }

        protected internal override void RecordFrame(RecordingSession session)
        {
            if (settings.HasErrors())
                return;

            if (m_Inputs.Count != 1)
                throw new Exception("Unsupported number of sources");
            // Store path name for this frame into a queue, as WriteFrame may be called
            // asynchronously later on, when the current frame is no longer the same (thus creating
            // a file name that isn't in sync with the session's current frame).
            m_PathQueue.Enqueue(Settings.FileNameGenerator.BuildAbsolutePath(session));
            base.RecordFrame(session);
        }

        protected override void WriteFrame(Texture2D tex)
        {
            byte[] bytes;

            Profiler.BeginSample("AOVRecorder.EncodeImage");
            try
            {
                switch (Settings.m_OutputFormat)
                {
                    case ImageRecorderSettings.ImageRecorderOutputFormat.EXR:
                    {
                        bytes = tex.EncodeToEXR(ImageRecorderSettings.ToNativeType(Settings.EXRCompression));
                        WriteToFile(bytes);
                        break;
                    }
                    case ImageRecorderSettings.ImageRecorderOutputFormat.PNG:
                        bytes = tex.EncodeToPNG();
                        WriteToFile(bytes);
                        break;
                    case ImageRecorderSettings.ImageRecorderOutputFormat.JPEG:
                        bytes = tex.EncodeToJPG(Settings.JpegQuality);
                        WriteToFile(bytes);
                        break;
                    default:
                        Profiler.EndSample();
                        throw new ArgumentOutOfRangeException();
                }
            }
            finally
            {
                Profiler.EndSample();
            }

            if (m_Inputs[0] is BaseRenderTextureInput || Settings.m_OutputFormat != ImageRecorderSettings.ImageRecorderOutputFormat.JPEG)
                UnityHelpers.Destroy(tex);
        }

        private void WriteToFile(byte[] bytes)
        {
            Profiler.BeginSample("AOVRecorder.WriteToFile");
            File.WriteAllBytes(m_PathQueue.Dequeue(), bytes);
            Profiler.EndSample();
        }
    }
}
