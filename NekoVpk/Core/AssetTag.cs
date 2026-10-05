using DotNet.Globbing;
using NekoVpk.ViewModels;
using ReactiveUI;
using SteamDatabase.ValvePak;
using System.Linq;

namespace NekoVpk.Core
{
    public class AssetTagProperty
    {
        public string Name { get; set; }

        public string Color { get; set; }

        public Glob[] Globs { get; set; }

        public string[]? Type { get; set; }

        public string[]? Mutex { get; set; }

        public AssetTagProperty(string name, Glob[] globs, string color = "", string[]? alias = null) {
            Name = name; Color = color; Globs = globs; Type = alias;
        }

        public AssetTagProperty(AssetTagProperty obj)
        {
            Name = obj.Name; Color = obj.Color; Globs = obj.Globs;
        }

        public override bool Equals(object? obj)
        {
            if (obj is AssetTagProperty tag) {
                return Name == tag.Name;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        static bool DeclaresMutex(AssetTagProperty from, AssetTagProperty to)
        {
            if (from.Mutex == null) return false;
            return from.Mutex.Contains("All") || from.Mutex.Contains(to.Name);
        }

        public static bool AreMutuallyExclusive(AssetTagProperty a, AssetTagProperty b)
        {
            if (a.Name == b.Name) return false;
            if (a.Type == null || b.Type == null) return false;
            if (!a.Type.Intersect(b.Type).Any()) return false;

            return DeclaresMutex(a, b) || DeclaresMutex(b, a);
        }

        public bool IsMatch(string path)
        {

            foreach (var glob in Globs)
            {
                if (glob.IsMatch(path))
                    return true;
            }
            return false;
        }

    }

    public class AssetTag: ViewModelBase
    {
        public readonly int Index;

        public AssetTagProperty Proporty { get => TaggedAssets.Tags[Index]; }

        public string Name { get => TaggedAssets.Tags[Index].Name; }

        public string Color { get => TaggedAssets.Tags[Index].Color; }

        public Glob[] Globs { get => TaggedAssets.Tags[Index].Globs; }

        public string[]? Type { get => TaggedAssets.Tags[Index]?.Type; }

        public string[]? Mutex { get => TaggedAssets.Tags[Index]?.Mutex; }

        bool _Enable;

        public bool Enable
        {
            get => _Enable; set => this.RaiseAndSetIfChanged(ref _Enable, value);
        }

        bool _IsModified;
        public bool IsModified
        {
            get => _IsModified; set => this.RaiseAndSetIfChanged(ref _IsModified, value);
        }

        public AssetTag(int index, bool enable = true)
        {
            _Enable = enable;
            Index = index;
        }

        public override bool Equals(object? obj)
        {
            if (obj is AssetTag tag && tag.Index == Index)
            {
                return true;
            }
            return false;
        }
        public override int GetHashCode()
        {
            return Index;
        }
    

    }
}
