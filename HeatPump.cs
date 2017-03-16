//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;

namespace HeatPumps
{
	public class ModuleHeatPump: PartModule//, IAnalyticPreview, IAnalyticTemperatureModifier
	{
		public class ResourceRate
		{
            protected string _name;
			protected string _unitName;
			protected double _rate;
			public int id
			{
				get 
				{
					return _name.GetHashCode ();
				}
			}

            public string name
            {
                get
                {
                    return _name;
                }
            }

            public string unitName
            {
                get
                {
                    return _unitName;
                }
            }

            public double rate
            {
                get
                {
                    return _rate;
                }
            }

			public ResourceRate(string name, double rate, string unitName)
			{
				this._name = name;
				this._rate = rate;
				this._unitName = unitName;
			}
			
		}
		
		[KSPField(isPersistant = false, guiActive = true, guiName = "Efficiency", guiUnits = "",   guiFormat = "")]
		public string efficiencyDisplay = "100";

		[KSPField(isPersistant = false, guiActive = true, guiName = "Heat Transfer", guiUnits = "",   guiFormat = "")]
		private string heatTransferDisplay = "0 W";

		[KSPAction ("Activate Heat Pump")]
		public void ActivateAction (KSPActionParam param)
		{
			if ((object)moduleDeployableRadiator != null)
				return;
			Activate ();
		}
		
		[KSPAction ("Shutdown Heat Pump")]
		public void ShutdownAction (KSPActionParam param)
		{
			if ((object)moduleDeployableRadiator != null)
				return;
			Shutdown ();
		}
		
		[KSPAction ("Toggle Heat Pump")]
		public void ToggleAction (KSPActionParam param)
		{
			if ((object)moduleDeployableRadiator != null)
				return;
			if(isActive)
				Shutdown ();
			else
				Activate ();
		}
		
		[KSPEvent(guiName = "Activate Heat Pump", guiActive = true)]
		public void Activate ()
		{
			if ((object)moduleDeployableRadiator != null)
				return;
			isActive = true;
			Events ["Shutdown"].active = true;
			Events ["Activate"].active = false;
			//Events ["Shutdown"].guiActive = true;
			//Events ["Activate"].guiActive = false;
		}
		
		[KSPEvent(guiName = "Shutdown Heat Pump", guiActive = true)]
		public void Shutdown ()
		{
			if ((object)moduleDeployableRadiator != null)
				return;
			isActive = false;
			Events ["Shutdown"].active = false;
			Events ["Activate"].active = true;
			//Events ["Shutdown"].guiActive = false;
			//Events ["Activate"].guiActive = true;
		}
		[KSPField()]
		public double radiatorMaxTempCap = 0.8;

		// TODO Deprecate this; would have performed function now performed by ModuleDeployableRadiator
		[KSPField]
		public bool useAnimationState = false;

		[KSPField]
		public bool useActionGroups = false;
		
		[KSPField(isPersistant = true)]
		public bool isActive = false;
		
		[KSPField(isPersistant = false)]
		public double heatTransfer = 0.1;

		[KSPField()]
		public double heatTransferCap = 1.0;

		[KSPField()]
		public double heatConductivity = 0.12;

		[KSPField()]
		public double skinInternalConductionMult = 0.001;
		
		public List<ResourceRate> inputResources;
        public List<ResourceRate> outputResources;

        public List<AttachNode> attachNodes = new List<AttachNode>();
		public List<string> attachNodeNames = new List<string>();

		private ModuleDeployableRadiator moduleDeployableRadiator;
		private double capTemp;
		private double skinCapTemp;
		private int radiatorCount;

		public bool IsActive
		{
			get
			{
				if((object)moduleDeployableRadiator != null)
                    return moduleDeployableRadiator.deployState == ModuleDeployableRadiator.DeployState.EXTENDED;
				else
					return isActive;
			}
		}

		static string FormatFlux(double flux)
		{
			if (flux >= 1000000000.0)
				return (flux / 1000000000.0).ToString("F2") + " TW";
			else if (flux >= 1000000.0)
				return (flux / 1000000.0).ToString("F2") + " GW";
			else if (flux >= 1000.0)
				return (flux / 1000.0).ToString("F2") + " MW";
			else if (flux >= 1.0)
				return (flux).ToString("F2") + " kW";
			else
				return (flux * 1000.0).ToString("F2") + " W";
			
		}

		public override string GetInfo ()
		{
			string s;
			s = "Heat Pump: " + heatTransfer + " kW\nRequirements:\n";
			foreach (ResourceRate resource in inputResources)
			{
				double _rate = resource.rate * heatTransfer;
				if(_rate > 1)
					s += "  " + resource.name + ": " + _rate.ToString ("F2") + " " + resource.unitName + "/s\n";
				else if(_rate > 0.01666667f)
					s += "  " + resource.name + ": " + (_rate * 60).ToString ("F2") + " " + resource.unitName + "/m\n";
				else
					s += "  " + resource.name + ": " + (_rate * 3600).ToString ("F2") + " " + resource.unitName + "/h\n";
			}
			
			return s;
		}
		
		public override void OnAwake ()
		{
			base.OnAwake ();
			inputResources = new List<ResourceRate> ();
            outputResources = new List<ResourceRate> ();
			attachNodes = new List<AttachNode>();
            heatTransferCap = Math.Max(heatTransferCap, heatTransfer); // should never be less than heatTransfer
		}
		
		public override void OnLoad (ConfigNode node)
		{
			base.OnLoad (node);
			foreach (ConfigNode n in node.GetNodes ("RESOURCE")) 
			{
				if(n.HasValue ("name") && n.HasValue ("rate")) 
				{
					double rate;
					string unitName = "";
					if (n.HasValue ("unitName"))
						unitName = n.GetValue ("unitName");
					else
						unitName = n.GetValue("name");
					double.TryParse (n.GetValue ("rate"), out rate);

					inputResources.Add (new ResourceRate(n.GetValue("name"), rate, unitName));
					print ("adding RESOURCE " + n.GetValue("name") + " = " + rate.ToString());
				}
			}

            foreach (ConfigNode n in node.GetNodes ("OUTPUT_RESOURCE")) {
                if (n.HasValue ("name") && n.HasValue ("rate")) {
                    double rate;
                    string unitName = "";
                    if (n.HasValue ("unitName"))
                        unitName = n.GetValue ("unitName");
                    else
                        unitName = n.GetValue ("name");
                    double.TryParse (n.GetValue ("rate"), out rate);

                    outputResources.Add (new ResourceRate (n.GetValue ("name"), rate, unitName));
                    print ("adding OUTPUT_RESOURCE " + n.GetValue ("name") + " = " + rate.ToString ());
                }
            }

            foreach (ConfigNode c in node.GetNodes("HEATPUMP_NODE"))
			{
				print("searching HEATPUMP_NODE");
				if (c.HasValue("name"))
				{
					string nodeName = c.GetValue("name");
					attachNodeNames.Add(nodeName);
                    print ("Adding HEATPUMP_NODE " + nodeName);
				}
				
			}
			
		}
		
		public override void OnStart (StartState state)
		{	
			base.OnStart (state);

			skinCapTemp = part.skinMaxTemp * radiatorMaxTempCap;
			capTemp = part.maxTemp * radiatorMaxTempCap;

			moduleDeployableRadiator = part.FindModuleImplementing<ModuleDeployableRadiator> ();
			radiatorCount = part.symmetryCounterparts.Count + 1;

			GameEvents.onVesselWasModified.Add (OnVesselWasModified);

			if ((object)moduleDeployableRadiator == null)
			{
				Events ["Shutdown"].active = isActive;
				Events ["Activate"].active = !isActive;
			}
			else
			{
				Events ["Shutdown"].active = false;
				Events ["Activate"].active = false;
				Actions["ToggleAction"].active = false;
				Actions["ActivateAction"].active = false;
				Actions["ShutdownAction"].active = false;
			}
			if(inputResources.Count == 0 && part.partInfo != null) 
			{
				inputResources = ((ModuleHeatPump) part.partInfo.partPrefab.Modules["ModuleHeatPump"]).inputResources;
			}
            if (outputResources.Count == 0 && part.partInfo != null) {
                outputResources = ((ModuleHeatPump)part.partInfo.partPrefab.Modules ["ModuleHeatPump"]).outputResources;
            }
            if (attachNodes.Count == 0)
			{
				foreach (string nodeName in attachNodeNames)
				{
					AttachNode node = part.FindAttachNode (nodeName);
					if ((object)node != null)
					{
						print ("Found AttachNode: " + nodeName);
						attachNodes.Add(node);
						if ((object)node.attachedPart != null)
						{
							print ("Found attached part: " + node.attachedPart.name);
							node.attachedPart.heatConductivity = Math.Min (heatConductivity, node.attachedPart.heatConductivity);
							node.attachedPart.skinInternalConductionMult = Math.Min (skinInternalConductionMult, node.attachedPart.skinInternalConductionMult);
						}
					}
				}
			}
			if ((object)part.srfAttachNode.attachedPart != null)
			{
				part.srfAttachNode.attachedPart.heatConductivity = Math.Min (heatConductivity, part.srfAttachNode.attachedPart.heatConductivity);
				part.srfAttachNode.attachedPart.skinInternalConductionMult = Math.Min (skinInternalConductionMult, part.srfAttachNode.attachedPart.skinInternalConductionMult);	
			}
		}

		public void OnVesselWasModified(Vessel v)
		{
			//if (v == part.vessel)
			//	radiatorCount = part.symmetryCounterparts.Count + 1;
		}
		
		void FixedUpdate()
		{
			if (!HighLogic.LoadedSceneIsFlight || !IsActive)
			{
				efficiencyDisplay = "0%";
				heatTransferDisplay = "0 W";
				return;
			}

            radiatorCount = 1;
            foreach (Part p in part.symmetryCounterparts)
            {
                ModuleHeatPump hp = p.Modules["ModuleHeatPump"] as ModuleHeatPump;
                if (hp.isActive)
                    radiatorCount += 1;
            }

			foreach (AttachNode attachNode in attachNodes)
			{
				Part targetPart = attachNode.attachedPart;
				
				if ((object)targetPart == null)
					continue;
				
				ProcessCooling(targetPart);
			}
			if (part.srfAttachNode.attachedPart != null)
				ProcessCooling(part.srfAttachNode.attachedPart);
		}
		
		public void ProcessCooling(Part targetPart)
		{
			double requested = 0d;
			double efficiency = 1d;
			double _heatTransfer = 0d;
			double conductionCompensation = 0d;

            double tempDelta = targetPart.temperature - (targetPart.maxTemp * targetPart.radiatorMax);

            if(tempDelta > 0d)
                _heatTransfer = Math.Min(Math.Min(heatTransfer * tempDelta, heatTransfer), heatTransferCap);

            // Throttle back if part is getting too hot.
            if (part.temperature >= capTemp || part.skinTemperature >= skinCapTemp)
                //conductionCompensation *= Math.Min (capTemp / part.temperature, skinCapTemp / part.skinTemperature);
                return;

            if (targetPart.thermalConductionFlux > 0.0)
				conductionCompensation  += (targetPart.thermalConductionFlux);
            if (targetPart.skinToInternalFlux > 0.0)
                conductionCompensation  += (targetPart.skinToInternalFlux);

			// Only counting radiators placed symmetrically for this to ensure that only heat pumps targeting the same parts are counted.
			conductionCompensation /= radiatorCount;

			foreach (ResourceRate resource in inputResources)
			{
				if(resource.rate > 0)
				{
					// Divided by PG.ConductionFactor because it gets WAY too expensive compensating for inflated conduction factors.
                    requested = (resource.rate * _heatTransfer) + (resource.rate * conductionCompensation / (PhysicsGlobals.ConductionFactor));
					requested *= TimeWarp.fixedDeltaTime;
					double available = part.RequestResource(resource.id, requested);
					if(efficiency > available / requested)
						efficiency = available / requested;
				}
			}
            // Was doing this for display purposes but not sure it's a good idea...
            //part.skinToInternalFlux -= (_heatTransfer + conductionCompensation) * efficiency;
			efficiencyDisplay = (efficiency).ToString ("P");
            heatTransferDisplay = FormatFlux((_heatTransfer + conductionCompensation) * efficiency) + "x" + radiatorCount.ToString();
			
            targetPart.AddThermalFlux(-(_heatTransfer + conductionCompensation) * efficiency);
            part.AddSkinThermalFlux ((_heatTransfer + conductionCompensation) * efficiency / PhysicsGlobals.ConductionFactor);
      
            foreach (ResourceRate resource in outputResources)
            {
                if (resource.rate > 0)
                {
                    requested = (resource.rate * _heatTransfer);
                    requested *= TimeWarp.fixedDeltaTime;
                    part.RequestResource (resource.id, -requested);
                }
            }
        }

		static void print(string msg)
		{
			MonoBehaviour.print("[HeatPump] " + msg);
		}
		/*
		// Analytic Interface
		public void SetAnalyticTemperature(double analyticTemp, double toBeInternal, double toBeSkin)
		{
		}

		public double GetSkinTemperature()
		{
		}

		public double GetInternalTemperature()
		{
		}
		*/

		// Analytic Preview Interface
        /*
        public void AnalyticInfo(double sunAndBodyIn, double backgroundRadiation, double radArea, double internalFlux, double convCoeff, double ambientTemp)
		{
		}

		public double InternalFluxAdjust()
		{
			//return previewInternalFluxAdjust;
		}
        */      
	}
}