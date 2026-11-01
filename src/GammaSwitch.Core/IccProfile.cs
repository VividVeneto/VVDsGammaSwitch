using System.Buffers.Binary;
using System.Text;

namespace GammaSwitch.Core;

public sealed record ProfileData(string Path, ushort[]? Gamma);

public static class IccProfile
{
    public static ProfileData Load(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("ICC 文件不存在。", path);
        if (file.Length > 16 * 1024 * 1024) throw new InvalidDataException("ICC 文件超过 16 MB 上限。");
        return Read(File.ReadAllBytes(path), Path.GetFullPath(path));
    }

    public static ProfileData Read(byte[] bytes, string path = "fixture.icc")
    {
        if (bytes.Length < 132 || Encoding.ASCII.GetString(bytes, 36, 4) != "acsp")
            throw new InvalidDataException("不是有效的 ICC/ICM 文件。");
        uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        ushort U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
        var declared = U32(0);
        if (declared < 132 || declared > bytes.Length) throw new InvalidDataException("ICC 文件已截断。");
        var count = U32(128);
        var directoryEnd = 132UL + count * 12UL;
        if (count > 4096 || directoryEnd > declared) throw new InvalidDataException("ICC 标签目录无效。");
        int found = -1, length = 0;
        for (var i = 0; i < count; i++)
        {
            var index = 132 + i * 12;
            var offset = U32(index + 4);
            var size = U32(index + 8);
            if (offset < directoryEnd || (ulong)offset + size > declared)
                throw new InvalidDataException("ICC 标签超出文件范围。");
            if (Encoding.ASCII.GetString(bytes, index, 4) != "vcgt") continue;
            if (found != -1) throw new InvalidDataException("ICC 包含重复 VCGT 标签。");
            found = (int)offset; length = (int)size;
        }
        if (found < 0) return new(path, null);
        if (length < 12 || Encoding.ASCII.GetString(bytes, found, 4) != "vcgt")
            throw new InvalidDataException("VCGT 标签头无效。");
        if (U32(found + 8) != 0)
            throw new NotSupportedException("此 ICC 使用公式型 VCGT；当前版本仅支持表格型曲线，请先转换为表格型。");
        if (length < 18) throw new InvalidDataException("VCGT 表格头已截断。");
        var channels = U16(found + 12);
        var entries = U16(found + 14);
        var width = U16(found + 16);
        if ((channels != 1 && channels != 3) || entries < 2 || (width != 1 && width != 2))
            throw new NotSupportedException("仅支持 1/3 通道、每点 8/16 位且至少 2 点的 VCGT。");
        if (18L + (long)channels * entries * width > length) throw new InvalidDataException("VCGT 曲线数据已截断。");
        var ramp = new ushort[768];
        for (var channel = 0; channel < 3; channel++)
        {
            var start = found + 18 + (channels == 1 ? 0 : channel * entries * width);
            ushort Sample(int index) => width == 1 ? (ushort)(bytes[start + index] * 257) : U16(start + index * 2);
            for (var i = 1; i < entries; i++)
                if (Sample(i) < Sample(i - 1)) throw new InvalidDataException("VCGT 曲线必须单调不减。");
            for (var i = 0; i < 256; i++)
            {
                var position = i * (entries - 1) / 255.0;
                var left = (int)position;
                var right = Math.Min(left + 1, entries - 1);
                ramp[channel * 256 + i] = (ushort)Math.Round(Sample(left) + (Sample(right) - Sample(left)) * (position - left), MidpointRounding.AwayFromZero);
            }
        }
        return new(path, ramp);
    }
}
