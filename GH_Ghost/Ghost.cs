using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;

using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel;
using Grasshopper;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Grasshopper.Plugin;
using Rhino;
using Rhino.NodeInCode;
using GH_IO.Serialization;

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

        protected bool complete = false; //TODO: serialize these
        protected bool running = false;
        protected bool interrupt = false;
        protected long comptime;

        protected bool reparams = false;
        protected ComponentFunctionInfo trgtcomp;
        protected IGH_DocumentObject srcobj;
        protected Guid srcobj_id = Guid.Empty;
        protected object[] evaluated;
        
        /// <summary>
        /// makes a dummy param if source input isn't working
        /// </summary>
        /// <param name="i">parameter index</param>
        /// <returns></returns>
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
        /// <summary>
        /// instantiate another from a parent object
        /// </summary>
        /// <param name="parent">the parent instance</param>
        /// <returns>the newly created instance of the same type as the parent object</returns>
        protected IGH_Param CopyParam(IGH_Param parent)
        {
            /*
            object o = Activator.CreateInstance(parent.GetType()); //create a new instance of a type from another instance
            var p = o as IGH_Param;*/
            // using generic below to make DA.GetDataTree work later
            var p = new Param_GenericObject
            {
                Name = parent.Name,
                Description = parent.Description,
                NickName = parent.NickName,
                Access = GH_ParamAccess.tree, // tree because component shouldn't compute more than once; handle tree manually
                MutableNickName = parent.MutableNickName,
                Simplify = parent.Simplify,
                Reverse = parent.Reverse,
                Optional = true // TODO: must handle no input situation!!
            };
            return p;
        }
        /// <summary>
        /// NodeInCode evaluation method, to run on separate thread
        /// </summary>
        /// <param name="prms">list of input trees</param>
        protected void GhostEval(object[] prms)
        {
            object locker = new object();
            if (prms.Length == 0 || trgtcomp==null)
            {
                complete = true && !interrupt;
                lock (locker) comptime = 0;
            }
            else if (running)
            {
                // shouldn't ever be here!
                complete = false;
                return; //skip invoke recompute
            }
            else
            {
                Stopwatch ticker = new Stopwatch();
                ticker.Start();
                running = true;

                object[] results; // each should be a tree
                lock (locker)
                    results = trgtcomp.Evaluate(prms, true, out string[] warns);
                
                running = false;
                complete = true && !interrupt;
                ticker.Stop();
                lock (locker)
                {
                    comptime = ticker.ElapsedMilliseconds;
                    evaluated = results;
                }
            }
            RhinoApp.InvokeOnUiThread(new Action<bool>(ExpireSolution), new object[] { true, });
        }
        

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Component", "C", "link up to a single component that needs to run in parallel\nlink to any output suffices; no data will transfer through here", GH_ParamAccess.tree);
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
            string consoletxt = "";
            IGH_Param src;
            if (Params.Input[0].SourceCount < 1)
            {
                // no incoming sources
                Message = "";
                return;
            } 
            else src = Params.Input[0].Sources[0];
            // check source object upon IO
            if (srcobj_id != Guid.Empty && (srcobj==null||trgtcomp==null))
            {
                srcobj = OnPingDocument().FindObject(srcobj_id, true);
                string srcname = srcobj.Name.Replace(" ", string.Empty);
                trgtcomp = Components.FindComponent(srcname);
                consoletxt += string.Format("\n{0}\n", trgtcomp.Name);
            }

            // test if source instance changed
            if (srcobj == null || srcobj.InstanceGuid != src.Attributes.GetTopLevel.DocObject.InstanceGuid)
            {
                srcobj = src.Attributes.GetTopLevel.DocObject;
                string srcname = srcobj.Name.Replace(" ", string.Empty);
                trgtcomp = Components.FindComponent(srcname);
                consoletxt += string.Format("\n{0}\n", trgtcomp.Name);
                if (trgtcomp == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, " Input is likely either a special component or from a plugin\n Cannot evaluate");
                    srcobj_id = Guid.Empty; //reset for IO purposes
                    DA.SetData(0, consoletxt);
                    Message = ""; // early return resets message
                    return;
                }
                // test if component needs to clean up old params
                if (Params.Input.Count > 1 || Params.Output.Count > 1)
                {
                    reparams = true; // this will flip back to false in AfterSolve->VarMaintenance
                    DA.SetData(0, consoletxt);
                    Message = ""; // early return resets message
                    return; // parameters must be re-setup now so ditch below
                }
            }

            // trgtcomp and srcobj should both have values now
            // test if user added all parameters
            if (!(srcobj is GH_Component srccomp))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, " Input is likely a special component\n Cannot evaluate");
                return;
            }
            else if (srccomp.Params.Input.Count != Params.Input.Count - 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, " Zoom in to add missing input parameter(s)");
                DA.SetData(0, trgtcomp.Name);
                return;
            }
            else if (srccomp.Params.Output.Count != Params.Output.Count - 1)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, " Zoom in to add hidden output parameter(s)");


            // get data, it's known now that user has added all missing input parameters
            object[] param_ins = new object[Params.Input.Count-1];
            for (int i=0; i < Params.Input.Count; i++)
            {
                if (i == 0) continue;
                DA.GetDataTree(i, out GH_Structure<IGH_Goo> tree);
                param_ins.SetValue(tree, i-1);
            }

            // evaluate
            if (!running && !complete)
            {
                Task.Run(() => GhostEval(param_ins));
                Message = "Started";
                interrupt = false;
            }
            else if (!running && complete)
            {
                complete = false;
                Message = "Finished";
                consoletxt += string.Format(
                    comptime==0?"that thing computed instantly...you really needed me here?":"\nlatest solution took {0}ms",
                    comptime);
            }
            else if (running && !complete)
            {
                Message = "Computing";
                interrupt = true;
                consoletxt += "\ninterruption detected\nsolution will restart once current task finishes";
            }
            else
            {
                // shouldn't reach here!
                running = false;
                complete = false;
                interrupt = false;
                AddRuntimeMessage( GH_RuntimeMessageLevel.Warning, " internal error" );
            }
            
            // set data
            if (evaluated == null)
            {
                DA.SetData(0, consoletxt);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, " no compute data yet\n wait for worker to finish or reset inputs");
                return;
            }
            for (int i = 0; i < Params.Output.Count; i++)
            {
                if (i == 0)
                {
                    DA.SetData(0, consoletxt);
                    continue;
                }
                if (evaluated[i - 1] is IGH_DataTree outtree)
                    DA.SetDataTree(i, outtree);
            }
        }

        protected override void AfterSolveInstance()
        {
            VariableParameterMaintenance();
            base.AfterSolveInstance();
        }

        
        #region add or destroy parameters
        public bool CanInsertParameter(GH_ParameterSide side, int i)
        {
            if (srcobj is GH_Component comp)
            {
                if (side == GH_ParameterSide.Input && i <= comp.Params.Input.Count && i == Params.Input.Count)
                    return true;
                else if (side == GH_ParameterSide.Output && i <= comp.Params.Input.Count &&i == Params.Output.Count)
                    return true;
                else
                    return false;
            }
            else return false;
            /*
            if (side == GH_ParameterSide.Input && i == Params.Input.Count) return true;
            else if (side == GH_ParameterSide.Output && i == Params.Output.Count) return true;
            else return false;*/
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
            List<IGH_Param> downstream = new List<IGH_Param>();
            if (reparams)
            {
                // this block deletes all variable params

                reparams = false;
                while (Params.Input.Count>1)
                {
                    Params.Input.Last().Sources.Clear();
                    Params.UnregisterInputParameter(Params.Input.Last(), true); // this modifies list length during looping
                }
                while (Params.Output.Count>1)
                {
                    //TODO: must unwire somehow!
                    foreach (var prm in Params.Output.Last().Recipients)
                        downstream.Add(prm);
                    Params.Output.Last().Recipients.Clear();
                    Params.UnregisterOutputParameter(Params.Output.Last(), true); // this modifies list length during looping
                }
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, " Target component reset\n Zoom in to add inputs/outputs again");
            }
            Params.OnParametersChanged();

            // schedule solution so downstream stuff unwire themselves from this
            if (OnPingDocument() is GH_Document ghdoc)
                ghdoc.ScheduleSolution(1, delegate {
                    foreach (var prm in downstream)
                        prm.RemoveAllSources();
                });
        }
        #endregion



        public override bool Write(GH_IWriter writer)
        {
            if (srcobj!=null)
                writer.SetGuid("srcobj_id", srcobj.InstanceGuid);
            return base.Write(writer);
        }
        public override bool Read(GH_IReader reader)
        {
            reader.TryGetGuid("srcobj_id", ref srcobj_id);
            return base.Read(reader);
        }

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
