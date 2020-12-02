﻿using System;
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
    public class Ghost : GH_Component
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
              "Run a component on another thread",
              "Maths", "Script")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Component", "C", "a single grasshopper component", GH_ParamAccess.list);
            pManager[0].DataMapping = GH_DataMapping.Flatten;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Message", "T", "usefully information", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
        }


        #region add or destroy parameters
        public bool CanInsertParameter(GH_ParameterSide side, int i)
        {
            if (side == GH_ParameterSide.Input && i == Params.Input.Count) return true;
            else return false;
        }

        public bool CanRemoveParameter(GH_ParameterSide side, int i)
        {
            if (side == GH_ParameterSide.Input && i == Params.Input.Count - 1) return true;
            else return false;
        }

        public IGH_Param CreateParameter(GH_ParameterSide side, int i)
        {
            //TODO: change implementation of this method
            Param_GenericObject newbranch = new Param_GenericObject
            {
                NickName = string.Format("B{0}", i),
                Name = "Branch",
                Description = "branch to append",
                Optional = true,
                Access = GH_ParamAccess.tree
            };
            Params.RegisterInputParam(newbranch, i);

            return newbranch;
        }

        public bool DestroyParameter(GH_ParameterSide side, int i)
        {
            return side == GH_ParameterSide.Input;
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
                // You can add image files to your project resources and access them like this:
                //return Resources.IconForThisComponent;
                return null;
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