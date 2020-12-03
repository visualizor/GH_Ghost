using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel;
using Grasshopper;
using Rhino.Geometry;
using Rhino;
using Rhino.NodeInCode;

// In order to load the result of this wizard, you will also need to
// add the output bin/ folder of this project to the list of loaded
// folder in Grasshopper.
// You can use the _GrasshopperDeveloperSettings Rhino command for that.

namespace GH_Ghost
{
    public class Ghost : GH_Component, IGH_VariableParameterComponent
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public Ghost()
          : base("Ghost Worker", "GhWkr",
              "Run a component on another thread from UI\nThat component will still be computed single-threaded",
              "Maths", "Script")
        {
        }

        protected ComponentFunctionInfo trgtcomp;
        protected IGH_DocumentObject srcobj;
        protected void UpdateCurrentParam()
        {

        }
        protected IGH_Param MakeDummyParam(int i)
        {
            Param_GenericObject nxt = new Param_GenericObject
            {
                NickName = string.Format("P{0}", i),
                Name = "A Parameter",
                Description = "meaningless parameter\nset target component first",
                Optional = true,
                Access = GH_ParamAccess.item,
            };
            Params.RegisterInputParam(nxt, i);
            return nxt;
        }
        protected IGH_Param CopyParam(IGH_Param parent)
        {
            object o = Activator.CreateInstance(parent.GetType()); //create a new instance of a type from another instance
            var p = o as IGH_Param;
            p.Name = parent.Name;
            p.Description = parent.Description;
            p.NickName = parent.NickName;
            p.Access = GH_ParamAccess.tree;
            p.MutableNickName = parent.MutableNickName;
            p.Simplify = parent.Simplify;
            p.Reverse = parent.Reverse;
            p.Optional = parent.Optional;
            return p;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Component", "C", "link up to the single component that needs to run in parallel", GH_ParamAccess.tree);
            pManager[0].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Message", "T", "usefully information", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_Param src;
            if (Params.Input[0].SourceCount < 1) return;
            else src = Params.Input[0].Sources[0];

            srcobj = src.Attributes.GetTopLevel.DocObject;
            string srcname = srcobj.Name.Replace(" ", string.Empty);
            trgtcomp = Components.FindComponent(srcname);
            if (trgtcomp==null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, " Input is likely either a special component or from a plugin\n Cannot evaluate");
                return;
            }
            
            DA.SetData(0, trgtcomp.Name);
        }


        #region add or destroy parameters
        public bool CanInsertParameter(GH_ParameterSide side, int i)
        {
            if (side == GH_ParameterSide.Input && i == Params.Input.Count) return true;
            else if (side == GH_ParameterSide.Output && i == Params.Output.Count) return true;
            else return false;
        }

        public bool CanRemoveParameter(GH_ParameterSide side, int i)
        {
            if (side == GH_ParameterSide.Input && Params.Input.Count == 1) return false;
            else if (side == GH_ParameterSide.Output && 1 == Params.Output.Count) return false;
            else if (side == GH_ParameterSide.Input && i == Params.Input.Count - 1) return true;
            else if (side == GH_ParameterSide.Output && i == Params.Output.Count - 1) return true;
            else return false;
        }

        public IGH_Param CreateParameter(GH_ParameterSide side, int i)
        {
            //TODO: finish this
            if (srcobj == null)
            {
                return MakeDummyParam(i);
            }
            else
            {
                if (!(srcobj is GH_Component src))
                    return MakeDummyParam(i);
                else
                    try
                    {
                        if (side == GH_ParameterSide.Input)
                            return CopyParam(src.Params.Input[i - 1]);
                        else if (side == GH_ParameterSide.Output)
                            return CopyParam(src.Params.Output[i - 1]);
                        else
                            return MakeDummyParam(i);
                    }
                    catch { return MakeDummyParam(i); }
            }
        }
        
        public bool DestroyParameter(GH_ParameterSide side, int i)
        {
            //return side == GH_ParameterSide.Input;
            return true;
        }

        public void VariableParameterMaintenance()
        {
            return;
        }
        #endregion


        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Properties.Resources.GhostWorker;
            }
        }

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("32a58ce7-c83f-432c-aba0-3a2ac7c8fd2f"); }
        }
    }
}
