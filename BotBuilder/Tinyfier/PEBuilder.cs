using System;
using AsmResolver;
using AsmResolver.DotNet.Builder;
using AsmResolver.IO;
using AsmResolver.PE;
using AsmResolver.PE.DotNet.Builder;
using AsmResolver.PE.DotNet.Metadata.Guid;
using AsmResolver.PE.File;
using AsmResolver.PE.File.Headers;

public partial class Tinyfier {
    private PEFile BuildPE() {
        //Build the tiny bot DLL by modifying some other parameters
        IPEImage tinyBotImg = new ManagedPEImageBuilder().CreateImage(Module).ConstructedImage ?? throw new InvalidOperationException("No tiny bot PEImage was built");
        tinyBotImg.MachineType = MachineType.Amd64; //This surpresses the native bootstrapping code - we change it back afterwards to maintain compat with other platforms
        tinyBotImg.Resources = null;
        tinyBotImg.Imports.Clear();
        tinyBotImg.Exports = null;
        tinyBotImg.Relocations.Clear();
        tinyBotImg.DebugData.Clear();

        if(tinyBotImg.DotNetDirectory?.Metadata?.TryGetStream(out GuidStream? guidStream) ?? false) {
            tinyBotImg.DotNetDirectory.Metadata.Streams.Remove(guidStream);
        }

        PEFile tinyBotPE = new ManagedPEFileBuilder().CreateFile(tinyBotImg);
        tinyBotPE.FileHeader.Machine = MachineType.I386; //Revert earlier change
        tinyBotPE.OptionalHeader.FileAlignment = tinyBotPE.OptionalHeader.SectionAlignment = 512;

        //Compress the DOS header
        tinyBotPE.DosHeader.NextHeaderOffset = DosHeader.MinimalDosHeaderLength;
        BinaryStreamReader dosHeaderReader = new BinaryStreamReader(tinyBotPE.DosHeader.WriteIntoArray());
        tinyBotPE.DosHeader = DosHeader.FromReader(ref dosHeaderReader);

        Log($"Built tinyfied PE file");
        return tinyBotPE;
    }
}