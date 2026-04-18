using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Graphics;
using Microsoft.Xna.Framework.Content.Pipeline.Processors;

namespace ContentPipeline
{
    [ContentProcessor(DisplayName = "Effect - Fxc")]
    public class FxcEffectProcessor : EffectProcessor
    {
        public override CompiledEffectContent Process(EffectContent input, ContentProcessorContext context)
        {
            string compiledFile = Path.Combine(
                Path.GetDirectoryName(input.Identity.SourceFilename),
                string.Format("{0}.fxc", Path.GetFileNameWithoutExtension(input.Identity.SourceFilename))
                );

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                if (File.Exists(compiledFile))
                {
                    return new CompiledEffectContent(File.ReadAllBytes(compiledFile));
                }

                throw new InvalidContentException("Compiling on a non-Windows platform requires a precompiled effect!", input.Identity);
            }
            
            string compiledTempFile = string.Format("{0}.fxc", Path.GetTempFileName());

            string toolPath = string.Format("{0}\\fxc.exe", Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = toolPath,
                    Arguments = string.Format("/T fx_2_0 \"{0}\" /Fo \"{1}\"", input.Identity.SourceFilename, compiledTempFile),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };

            process.Start();

            process.WaitForExit();

            if (!File.Exists(compiledTempFile))
            {
                string output = process.StandardError.ReadToEnd();

                throw new InvalidContentException(output, input.Identity);
            }

            byte[] buffer = File.ReadAllBytes(compiledTempFile);

            File.WriteAllBytes(compiledFile, buffer);

            return new CompiledEffectContent(buffer);
        }
    }
}