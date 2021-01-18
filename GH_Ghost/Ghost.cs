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
using System.Windows.Forms;

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
          : base("Ghost Worker", "Ghost",
              "Run a component on another thread from UI\nTask itself still single-threaded",
              "Maths", "Script")
        {
        }

        public static int WorkerCount { get; private set; } = 0;
        /* cross thread event bug
        public static int Workercount
        {
            get { return wc; }
            set
            {
                if (value.GetType() == typeof(int) && value >= 0)
                {
                    wc = value;
                    OnWorkerChanged();
                }
            }
        }*/
        
        protected bool complete = false;
        protected bool running = false;
        protected bool interrupt = false;
        protected bool trigger = false; // whether to queue next solution

        protected string comptime;
        protected string[] workerwarnings = new string[] { };
        protected bool reparams = false;
        protected ComponentFunctionInfo trgtcomp;
        protected IGH_DocumentObject srcobj;
        protected Guid srcobj_id = Guid.Empty; // for IO only
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
            lock (locker) WorkerCount++;

            // actual work in this block
            if (prms.Length == 0 || trgtcomp==null)
            {
                complete = true && !interrupt;
                lock (locker) comptime = " 0ms";
            }
            else if (running)
            {
                // shouldn't ever be here!
                complete = false;
                lock (locker) workerwarnings = new string[] { "Somehow a second ghost tried to start a task. It's been stopped.", };
                return; //skip invoke recompute
            }
            else
            {
                Stopwatch ticker = new Stopwatch();
                ticker.Start();
                running = true;

                object[] results; // each should be a tree
                lock (locker)
                    results = trgtcomp.Evaluate(prms, true, out workerwarnings);
                
                running = false;
                complete = true && !interrupt;
                ticker.Stop();
                lock (locker)
                {
                    comptime = WatchParse(ticker);
                    evaluated = results;
                }
            } // end works

            lock (locker) WorkerCount--;
            RhinoApp.InvokeOnUiThread(new Action<bool>(ExpireSolution), new object[] { true, });
        }
        /// <summary>
        /// parse the stopwatch ellapsed time
        /// </summary>
        /// <param name="w">stop watch object</param>
        /// <returns>legible string of the time span</returns>
        protected string WatchParse(Stopwatch w)
        {
            if (w.ElapsedMilliseconds < 1000)
                return string.Format(" {0}ms", w.ElapsedMilliseconds);
            else
            {
                var h = w.Elapsed.Hours;
                var m = w.Elapsed.Minutes;
                var s = w.Elapsed.Seconds;
                var ms = w.Elapsed.Milliseconds;
                return string.Format(" {0}{1}{2}",
                    h == 0 ? "" : h.ToString() + "hrs ",
                    m == 0 ? "" : m.ToString() + "mins ",
                    s == 0 ? "" : (s+ms/1000.0).ToString() + "secs"
                    );
            }
        }

        /* cross thread event bug
        public delegate void GenericHandler();
        public static event GenericHandler WorkerChanged = delegate { };
        protected static void OnWorkerChanged()
        {
            WorkerChanged.Invoke();
        } */

        private void OnUnblock(object s, EventArgs e)
        {
            trigger = !trigger;
        }
        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            try
            {
                ToolStripMenuItem recomp = menu.Items.Add("Unblock recompute queueing", null, OnUnblock) as ToolStripMenuItem;
                recomp.Checked = trigger;
            }
            catch { }
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
            pManager.AddTextParameter("TextOut", "T", "usefully information", GH_ParamAccess.tree);
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
                if (trgtcomp != null)
                    consoletxt += string.Format("{0}\n", trgtcomp.Name);
                else
                    srcobj = null;
            }

            // test if source instance changed
            if (srcobj == null || srcobj.InstanceGuid != src.Attributes.GetTopLevel.DocObject.InstanceGuid)
            {
                srcobj = src.Attributes.GetTopLevel.DocObject;
                string srcname = srcobj.Name.Replace(" ", string.Empty);
                trgtcomp = Components.FindComponent(srcname);
                if (trgtcomp == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, " Input is likely either a special component or from a plugin\n Cannot evaluate");
                    srcobj_id = Guid.Empty; //reset for IO purposes
                    DA.SetData(0, consoletxt);
                    Message = ""; // early return resets message
                    return;
                }
                consoletxt += string.Format("{0}\n", trgtcomp.Name);
                // test if component needs to clean up old params
                if (Params.Input.Count > 1 || Params.Output.Count > 1)
                {
                    reparams = true; // this will flip back to false in AfterSolve->VarMaintenance
                    DA.SetData(0, consoletxt);
                    Message = ""; // early return resets message
                    return; // parameters must be re-setup now so ditch below
                }
            }
            else consoletxt += string.Format("{0}\n", trgtcomp.Name);
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
                workerwarnings = new string[] { }; // reset message from threaded worker
                Task.Run(() => GhostEval(param_ins));
                Message = "Started";
                interrupt = false;
            }
            else if (!running && complete)
            {
                complete = false; // sets up next solution
                Message = "Finished";
                consoletxt += string.Format(
                    comptime == " 0ms" ? "\n(that thing computed instantly...you really needed me here?)" : "\n(latest solution took{0})",
                    comptime);
                try { consoletxt += "\nRuntime warnings >>>\n" + string.Join("\n", workerwarnings); }
                catch (ArgumentNullException) { }
                catch (NullReferenceException) { }
            }
            else if (running && !complete)
            {
                Message = "Computing";
                if (trigger)
                {
                    interrupt = true;
                    consoletxt += "\n(interruption detected\nsolution will restart once current task finishes)";
                }
                else
                    consoletxt += "\n(interruption not handled\n unblock queueing in context menu if needed)";
            }
            else
            {
                // shouldn't reach here!
                running = false;
                complete = false;
                interrupt = false;
                AddRuntimeMessage( GH_RuntimeMessageLevel.Error, " internal error\n try recompute with better inputs");
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
                else if (!running)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, " no valid result yet");
            }
        }

        protected override void AfterSolveInstance()
        {
            VariableParameterMaintenance();
            base.AfterSolveInstance();
        }

        protected override void ExpireDownStreamObjects()
        {
            if (!running && complete)
                base.ExpireDownStreamObjects();
            else
                foreach (IGH_Param r in Params.Output[0].Recipients)
                    r.Attributes.GetTopLevel.DocObject.ExpireSolution(true); // only expire the message output when thread is running
        }

        #region add or destroy parameters
        public bool CanInsertParameter(GH_ParameterSide side, int i)
        {
            if (srcobj is GH_Component comp)
            {
                if (side == GH_ParameterSide.Input && i <= comp.Params.Input.Count && i == Params.Input.Count)
                    return true;
                else if (side == GH_ParameterSide.Output && i <= comp.Params.Output.Count && i == Params.Output.Count)
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
                // this block deletes all variable params wires
                reparams = false;
                while (Params.Input.Count>1)
                {
                    Params.Input.Last().Sources.Clear();
                    Params.UnregisterInputParameter(Params.Input.Last(), true); // this modifies list length during looping
                }
                while (Params.Output.Count>1)
                {
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
            writer.SetBoolean("trigger", trigger);
            if (srcobj!=null)
                writer.SetGuid("srcobj_id", srcobj.InstanceGuid);
            return base.Write(writer);
        }
        public override bool Read(GH_IReader reader)
        {
            reader.TryGetBoolean("trigger", ref trigger);
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
