using kOS.Module;
using kOS.Safe.Encapsulation;
using kOS.Safe.Encapsulation.Suffixes;
using kOS.Safe.Exceptions;
using kOS.Suffixed.PartModuleField;
using kOS.Utilities;
using System;
using System.Linq;
using System.Collections.Generic;
using kOS.Safe.Compilation.KS;
using UnityEngine;

namespace kOS.Suffixed.Part
{
    [kOS.Safe.Utilities.KOSNomenclature("Part")]
    public class PartValue : Structure, IKOSTargetable
    {
        public SharedObjects Shared { get; private set; }
        public global::Part Part { get; private set; }
        public PartValue Parent { get; private set; }
        public DecouplerValue Decoupler { get; private set; }
        public ListValue<PartValue> Children { get; private set; }
        public Structure ParentValue { get { return (Structure)Parent ?? StringValue.None; } }
        public Structure DecouplerValue { get { return (Structure)Decoupler ?? StringValue.None; } }
        public int DecoupledIn { get { return (Decoupler != null) ? Decoupler.Part.inverseStage : -1; } }

        /// <summary>
        /// Do not call! VesselTarget.ConstructPart uses this, would use `friend VesselTarget` if this was C++!
        /// </summary>
        internal PartValue(SharedObjects shared, global::Part part, PartValue parent, DecouplerValue decoupler)
        {
            Shared = shared;
            Part = part;
            Parent = parent;
            Decoupler = decoupler;
            RegisterInitializer(PartInitializeSuffixes);
            Children  = new ListValue<PartValue>();
        }

        private void PartInitializeSuffixes()
        {
            AddSuffix("CONTROLFROM", new NoArgsVoidSuffix(ControlFrom));
            AddSuffix("NAME", new Suffix<StringValue>(() => Part.name));
            AddSuffix("FUELCROSSFEED", new Suffix<BooleanValue>(() => Part.fuelCrossFeed));
            AddSuffix("TITLE", new Suffix<StringValue>(() => Part.partInfo.title));
            AddSuffix("STAGE", new Suffix<ScalarValue>(() => Part.inverseStage));
            AddSuffix("CID", new Suffix<StringValue>(() => Part.craftID.ToString()));
            AddSuffix("UID", new Suffix<StringValue>(() => Part.flightID.ToString()));
            AddSuffix("ROTATION", new Suffix<Direction>(() => new Direction(Part.transform.rotation)));
            AddSuffix("POSITION", new Suffix<Vector>(() => GetPosition()));
            AddSuffix("TAG", new SetSuffix<StringValue>(GetTagName, SetTagName));
            AddSuffix("FACING", new Suffix<Direction>(() => GetFacing()));
            AddSuffix("BOUNDS", new Suffix<BoundsValue>(GetBoundsValue));
            AddSuffix("RESOURCES", new Suffix<ListValue>(() => GatherResources(Part)));
            AddSuffix("TARGETABLE", new Suffix<BooleanValue>(() => Part.Modules.OfType<ITargetable>().Any()));
            AddSuffix("SHIP", new Suffix<VesselTarget>(() => VesselTarget.CreateOrGetExisting(Part.vessel, Shared)));
            AddSuffix("HASMODULE", new OneArgsSuffix<BooleanValue, StringValue>(HasModule));
            AddSuffix("GETMODULE", new OneArgsSuffix<PartModuleFields, StringValue>(GetModule));
            AddSuffix("GETMODULEBYINDEX", new OneArgsSuffix<PartModuleFields, ScalarValue>(GetModuleIndex));
            AddSuffix(new[] { "MODULES", "ALLMODULES" }, new Suffix<ListValue>(GetAllModules, "A List of all the modules' names on this part"));
            AddSuffix("PARENT", new Suffix<Structure>(() => ParentValue, "The parent part of this part"));
            AddSuffix(new[] { "DECOUPLER", "SEPARATOR" }, new Suffix<Structure>(() => DecouplerValue, "The part that will decouple/separate this part when activated"));
            AddSuffix(new[] { "DECOUPLEDIN", "SEPARATEDIN" }, new Suffix<ScalarValue>(() => DecoupledIn));
            AddSuffix("HASPARENT", new Suffix<BooleanValue>(() => Part.parent != null, "Tells you if this part has a parent, is used to avoid null exception from PARENT"));
            AddSuffix("CHILDREN", new Suffix<ListValue<PartValue>>(() => PartValueFactory.ConstructGeneric(Part.children, Shared), "A LIST() of the children parts of this part"));
            AddSuffix("DRYMASS", new Suffix<ScalarValue>(() => Part.GetDryMass(), "The Part's mass when empty"));
            AddSuffix("MASS", new Suffix<ScalarValue>(() => Part.CalculateCurrentMass(), "The Part's current mass"));
            AddSuffix("WETMASS", new Suffix<ScalarValue>(() => Part.GetWetMass(), "The Part's mass when full"));
            AddSuffix("HASPHYSICS", new Suffix<BooleanValue>(() => Part.HasPhysics(), "Is this a strange 'massless' part"));
        }

        public BoundsValue GetBoundsValue()
        {
            // Our normal facings use Z for forward, but parts use Y for forward:
            Quaternion rotateYToZ = Quaternion.FromToRotation(Vector2.up, Vector3.forward);

            Bounds unionBounds = new Bounds();

            MeshFilter[] meshes = Part.GetComponentsInChildren<MeshFilter>();
            for (int meshIndex = 0; meshIndex < meshes.Length; ++meshIndex)
            {
                MeshFilter mesh = meshes[meshIndex];
                Bounds bounds = mesh.mesh.bounds;

                // Part meshes could be scaled as well as rotated (the mesh might describe a
                // part that's 1 meter wide while the real part is 2 meters wide, and has a scale of 2x
                // encoded into its transform to do this).  Because of this, the only really
                // reliable way to get the real shape is to let the transform do its work on all 6 corners
                // of the bounding box, transforming them with the mesh's transform, then back-calculating
                // from that world-space result back into the part's own reference frame to get the bounds
                // relative to the part.
                Vector3 center = bounds.center;

                // This triple-nested loop visits all 8 corners of the box:
                for (int signX = -1; signX <= 1; signX += 2) // -1, then +1
                    for (int signY = -1; signY <= 1; signY += 2) // -1, then +1
                        for (int signZ = -1; signZ <= 1; signZ += 2) // -1, then +1
                        {
                            Vector3 corner = center + new Vector3(signX * bounds.extents.x, signY * bounds.extents.y, signZ * bounds.extents.z);
                            Vector3 worldCorner = mesh.transform.TransformPoint(corner);
                            Vector3 partCorner = rotateYToZ * Part.transform.InverseTransformPoint(worldCorner);

                            // Stretches the bounds we're making (which started at size zero in all axes),
                            // just big enough to include this corner:
                            unionBounds.Encapsulate(partCorner);
                        }
            }

            Vector min = new Vector(unionBounds.min);
            Vector max = new Vector(unionBounds.max);
            return new BoundsValue(min, max, delegate { return GetPosition() + new Vector(Part.boundsCentroidOffset); }, delegate { return GetFacing(); }, Shared);
        }


        public void ThrowIfNotCPUVessel()
        {
            if (Part.vessel.id != Shared.Vessel.id)
                throw new KOSWrongCPUVesselException();
        }

        private PartModuleFields GetModule(StringValue modName)
        {
            foreach (PartModule mod in Part.Modules)
            {
                if (string.Equals(mod.moduleName, modName, StringComparison.OrdinalIgnoreCase))
                {
                    return PartModuleFieldsFactory.Construct(mod, Shared);
                }
            }
            throw new KOSLookupFailException("module", modName.ToUpper(), this);
        }

        private BooleanValue HasModule(StringValue modName)
        {
            foreach (PartModule mod in Part.Modules)
            {
                if (string.Equals(mod.moduleName, modName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private PartModuleFields GetModuleIndex(ScalarValue moduleIndex)
        {
            if (moduleIndex < Part.Modules.Count)
            {
                return PartModuleFieldsFactory.Construct(Part.Modules.GetModule(moduleIndex), Shared);
            }
            throw new KOSLookupFailException("module", string.Format("MODULEINDEX[{0}]", moduleIndex), this);
        }

        public StringValue GetTagName() // public because I picture this being a useful API method later
        {
            KOSNameTag tagModule = Part.Modules.OfType<KOSNameTag>().FirstOrDefault();
            return tagModule == null ? string.Empty : tagModule.nameTag;
        }

        private void SetTagName(StringValue value)
        {
            ThrowIfNotCPUVessel();
            KOSNameTag tagModule = Part.Modules.OfType<KOSNameTag>().FirstOrDefault();
            if (tagModule != null) tagModule.nameTag = value;
        }

        public override string ToString()
        {
            string tagName = GetTagName();
            if (string.IsNullOrEmpty(tagName))
                return string.Format("PART({0},uid={1})", Part.name, Part.uid());
            return string.Format("PART({0},tag={1})", Part.name, tagName);
        }

        public virtual ITargetable Target
        {
            get
            {
                return Part.Modules.OfType<ITargetable>().FirstOrDefault();
            }
        }

        public Direction GetFacing()
        {
            // Our normal facings use Z for forward, but parts use Y for forward:
            Quaternion rotateZToY = Quaternion.FromToRotation(Vector3.forward, Vector3.up);
            Quaternion newRotation = Part.transform.rotation * rotateZToY;
            return new Direction(newRotation);
        }

        public Vector GetPosition()
        {
            return new Vector(Part.transform.position - Shared.Vessel.CoMD);
        }

        private void ControlFrom()
        {
            ThrowIfNotCPUVessel();
            var dockingModule = Part.Modules.OfType<ModuleDockingNode>().FirstOrDefault();
            var commandModule = Part.Modules.OfType<ModuleCommand>().FirstOrDefault();

            if (commandModule != null)
            {
                commandModule.MakeReference();
            }
            else if (dockingModule != null)
            {
                dockingModule.MakeReferenceTransform();
            }
            else
            {
                throw new KOSCommandInvalidHereException(LineCol.Unknown(), "CONTROLFROM", "a generic part value", "a docking port or command part");
            }
        }

        private ListValue GatherResources(global::Part part)
        {
            var resources = new ListValue();
            // eliminate enumerators, use index based access
            for (int i = 0; i < part.Resources.Count; i++)
            {
                resources.Add(new SingleResourceValue(part.Resources[i]));
            }
            return resources;
        }

        private ListValue GetAllModules()
        {
            var returnValue = new ListValue();
            foreach (PartModule mod in Part.Modules)
            {
                returnValue.Add(new StringValue(mod.moduleName));
            }
            return returnValue;
        }

        protected bool Equals(PartValue other)
        {
            return Equals(Part, other.Part);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((PartValue)obj);
        }

        public override int GetHashCode()
        {
            return (Part != null ? Part.GetHashCode() : 0);
        }

        public static bool operator ==(PartValue left, PartValue right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(PartValue left, PartValue right)
        {
            return !Equals(left, right);
        }

    }
}
