"""Create a minimal valid IFC2x3 file for watcher testing."""
content = """ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');
FILE_NAME('test_minimal.ifc','2026-01-01T00:00:00',(''),(''),'IfcOpenShell','IfcOpenShell','');
FILE_SCHEMA(('IFC2X3'));
ENDSEC;
DATA;
#1=IFCPROJECT('0YvCtVUKr4jAilsiy$6drx',$,'Test Project',$,$,$,$,(#2),#3);
#2=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-5,#4,$);
#3=IFCUNITASSIGNMENT((#5,#6,#7));
#4=IFCAXIS2PLACEMENT3D(#8,$,$);
#5=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
#6=IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.);
#7=IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.);
#8=IFCCARTESIANPOINT((0.,0.,0.));
#9=IFCSITE('1lBi$tbhf3DBjGJyiGfPLq',$,'Site',$,$,#10,$,$,.ELEMENT.,$,$,$,$,$);
#10=IFCLOCALPLACEMENT($,#11);
#11=IFCAXIS2PLACEMENT3D(#12,$,$);
#12=IFCCARTESIANPOINT((0.,0.,0.));
#13=IFCBUILDING('2lBi$tbhf3DBjGJyiGfPLq',$,'Building',$,$,#14,$,$,.ELEMENT.,$,$,$);
#14=IFCLOCALPLACEMENT(#10,#15);
#15=IFCAXIS2PLACEMENT3D(#16,$,$);
#16=IFCCARTESIANPOINT((0.,0.,0.));
#17=IFCBUILDINGSTOREY('3lBi$tbhf3DBjGJyiGfPLq',$,'Level 1',$,$,#18,$,$,.ELEMENT.,0.);
#18=IFCLOCALPLACEMENT(#14,#19);
#19=IFCAXIS2PLACEMENT3D(#20,$,$);
#20=IFCCARTESIANPOINT((0.,0.,0.));
#21=IFCWALL('4lBi$tbhf3DBjGJyiGfPLq',$,'Wall 001',$,'Basic Wall',$,$,$);
#22=IFCWALL('5lBi$tbhf3DBjGJyiGfPLq',$,'Wall 002',$,'Basic Wall',$,$,$);
#23=IFCDOOR('6lBi$tbhf3DBjGJyiGfPLq',$,'Door 001',$,'Door',$,$,$,$,$);
#24=IFCRELAGGREGATES('7lBi$tbhf3DBjGJyiGfPLq',$,$,$,#1,(#9));
#25=IFCRELAGGREGATES('8lBi$tbhf3DBjGJyiGfPLq',$,$,$,#9,(#13));
#26=IFCRELAGGREGATES('9lBi$tbhf3DBjGJyiGfPLq',$,$,$,#13,(#17));
#27=IFCRELCONTAINEDINSPATIALSTRUCTURE('AlBi$tbhf3DBjGJyiGfPLq',$,$,$,(#21,#22,#23),#17);
ENDSEC;
END-ISO-10303-21;
"""
import pathlib
out = pathlib.Path("IFC_DROP/minimal.ifc")
out.parent.mkdir(exist_ok=True)
out.write_text(content)
print(f"Written {out} ({out.stat().st_size} bytes)")
