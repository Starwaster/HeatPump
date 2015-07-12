//#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;

namespace RealFuels
{
	public class ModuleHeatPump: PartModule
	{
		public class ResourceRate
		{
			public string name;
			public string unitName;
			public double rate;
			public int id
			{
				get 
				{
					return name.GetHashCode ();
				}
			}
			public ResourceRate(string name, double rate, string unitName)
			{
				this.name = name;
				this.rate = rate;
				this.unitName = unitName;
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

		[KSPField]
		public bool useAnimationState = false;

		[KSPField]
		public bool useActionGroups = false;
		
		[KSPField(isPersistant = true)]
		public bool isActive = false;
		
		[KSPField(isPersistant = false)]
		public double heatTransfer = 0.0;

		[KSPField()]
		public double heatConductivity = 0.12;

		[KSPField()]
		public bool legacy = false;

		public List<ResourceRate> resources;
		
		public List<AttachNode> attachNodes = new List<AttachNode>();
		public List<string> attachNodeNames = new List<string>();

		private ModuleDeployableRadiator moduleDeployableRadiator;

		public bool IsActive
		{
			get
			{
				if((object)moduleDeployableRadiator != null)
					return moduleDeployableRadiator.panelState == ModuleDeployableRadiator.panelStates.EXTENDED;
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
			foreach (ResourceRate resource in resources)
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
			resources = new List<ResourceRate> ();
			attachNodes = new List<AttachNode>();
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

					resources.Add (new ResourceRate(n.GetValue("name"), rate, unitName));
					print ("adding RESOURCE " + n.GetValue("name") + " = " + rate.ToString());
				}
			}
			foreach (ConfigNode c in node.GetNodes("HEATPUMP_NODE"))
			{
				print("searching HEATPUMP_NODE");
				if (c.HasValue("name"))
				{
					string nodeName = c.GetValue("name");
					print("adding " + nodeName);
					attachNodeNames.Add(nodeName);
				}
				
			}
			
		}
		
		public override void OnStart (StartState state)
		{	
			base.OnStart (state);

			moduleDeployableRadiator = part.FindModuleImplementing<ModuleDeployableRadiator> ();

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
			if(resources.Count == 0 && part.partInfo != null) 
			{
				//if(part.partInfo.partPrefab.Modules.Contains ("ModuleHeatPump")) // derrrrr why do I have to check if this contains ModuleHeatPump?
					resources = ((ModuleHeatPump) part.partInfo.partPrefab.Modules["ModuleHeatPump"]).resources;
			}
			if (attachNodes.Count == 0)
			{
				foreach (string nodeName in attachNodeNames)
				{
					AttachNode node = part.findAttachNode(nodeName);
					if ((object)node != null)
					{
						print ("Found AttachNode: " + nodeName);
						attachNodes.Add(node);
						if ((object)node.attachedPart != null)
						{
							print ("Found attached part: " + node.attachedPart.name);
							node.attachedPart.heatConductivity = heatConductivity;
						}
					}
				}
			}
			if ((object)part.srfAttachNode.attachedPart != null)
				part.srfAttachNode.attachedPart.heatConductivity = heatConductivity;	
		}
		
		void FixedUpdate()
		{
			if (!HighLogic.LoadedSceneIsFlight || !IsActive)
			{
				efficiencyDisplay = "0%";
				heatTransferDisplay = "0 W";
				return;
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
			heatTransferDisplay = FormatFlux (part.thermalInternalFlux);
			part.skinTemperature += (part.thermalInternalFlux * part.skinThermalMassRecip * TimeWarp.fixedDeltaTime);
			// TODO Kludgy way to see how much heat is being processed. Will fix it properly later
			part.thermalInternalFlux = 0.0;
		}
		
		public void ProcessCooling(Part targetPart)
		{
			if (part.temperature >= part.maxTemp * radiatorMaxTempCap || part.skinTemperature >= part.skinMaxTemp * radiatorMaxTempCap)
				return;

			double requested = 0d;
			double efficiency = 1d;
			double _heatTransfer = heatTransfer;
			double conductionCompensation = 0d;

			if (legacy)
				_heatTransfer *= (targetPart.temperature) / (targetPart.temperature + 27);

			if (targetPart.thermalConductionFlux + targetPart.skinToInternalFlux > 0.0)
				conductionCompensation  = (targetPart.thermalConductionFlux + targetPart.skinToInternalFlux);

			foreach (ResourceRate resource in resources)
			{
				if(resource.rate > 0)
				{
					requested = (resource.rate * _heatTransfer) + (resource.rate * conductionCompensation / PhysicsGlobals.ConductionFactor); // Because it gets WAY too expensive compensating for inflated conduction factors.
					requested *= TimeWarp.fixedDeltaTime;
					double available = part.RequestResource(resource.id, requested);
					if(efficiency > available / requested)
						efficiency = available / requested;
				}
			}
			efficiencyDisplay = (efficiency).ToString ("P");

			// Uses KSP 1.0 InternalHeatFlux now
			targetPart.AddThermalFlux(-_heatTransfer * efficiency);
			part.AddThermalFlux (_heatTransfer * efficiency);
		}
		static void print(string msg)
		{
			MonoBehaviour.print("[HeatPump] " + msg);
		}
	}
}