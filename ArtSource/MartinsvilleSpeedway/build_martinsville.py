"""Blender CLI builder. Editable driving geometry only; dimensions are meters."""
import bpy, json, math, bisect
from pathlib import Path
from mathutils import Vector

OUT = Path(__file__).resolve().parent
LAP, S, W = .526 * 1609.344, 800 * .3048, 55 * .3048
R = (LAP - 2*S)/(2*math.pi)
A = math.pi*R
PIT_OFFSET, PIT_WIDTH, APRON = 20., 12., 8.
T3 = 2*S+A
TURN_SPANS = ((S, S+A), (T3, LAP))
BANK_DEGREES = 12.
BANK_RUNOUT_METERS = 60.
ENTRY, ROAD_START = T3-65, T3+30
ROAD_END, EXIT = LAP+S+A-30, LAP+S+A+65
FINISH = S*.5
RESTART = (FINISH-43, FINISH-12)
RECORD = 18.898

def smooth(t):
    t=max(0,min(1,t)); return t*t*(3-2*t)
def quint(t):
    t=max(0,min(1,t)); return t**3*(10+t*(-15+6*t))
def stations(a,b,spacing=1):
    n=math.ceil((b-a)/spacing); return [a+(b-a)*i/n for i in range(n+1)]
def base(s):
    s%=LAP
    if s<S: return Vector((-S/2+s,-R))
    if s<S+A:
        a=-math.pi/2+(s-S)/R; return Vector((S/2+R*math.cos(a),R*math.sin(a)))
    if s<2*S+A: return Vector((S/2-(s-S-A),R))
    a=math.pi/2+(s-2*S-A)/R; return Vector((-S/2+R*math.cos(a),R*math.sin(a)))
def blended(s):
    return sum((base(s+2*i)*(5-abs(i)) for i in range(-4,5)),Vector((0,0)))/25
def catmull(a,b,c,d,t):
    return .5*(2*b+(c-a)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)
controls=[blended(i*LAP/240) for i in range(240)]
dense=[catmull(controls[(i-1)%240],controls[i],controls[(i+1)%240],controls[(i+2)%240],j/24)
       for i in range(240) for j in range(24)]
factor=LAP/sum((b-a).length for a,b in zip(dense,dense[1:]+dense[:1]))
controls=[p*factor for p in controls]
def center(s):
    f=(s%LAP)/LAP*240; i=int(f); t=f-i
    return catmull(controls[(i-1)%240],controls[i],controls[(i+1)%240],controls[(i+2)%240],t)
def inward(s):
    t=(center(s+.05)-center(s-.05)).normalized(); return Vector((-t.y,t.x))
def point(s,offset): return center(s)+inward(s)*offset
# Banking and markers are measured on the final Catmull-Rom driving line.
# Use Unity's 24 samples per layout segment to map authoring stations to lap fractions.
samples=[center(i*LAP/5760) for i in range(5761)]
lengths=[0.]
for a,b in zip(samples,samples[1:]):
    lengths.append(lengths[-1]+(b-a).length)

def fraction(s):
    f=(s%LAP)*5760/LAP
    i=int(f)
    return (lengths[i]+(lengths[i+1]-lengths[i])*(f-i))/lengths[-1]

def station(f):
    if f >= 1.:
        return LAP
    distance=f*lengths[-1]
    i=bisect.bisect_right(lengths,distance)-1
    t=(distance-lengths[i])/(lengths[i+1]-lengths[i])
    return (i+t)*LAP/5760

banking_transitions=[]
runout=BANK_RUNOUT_METERS/LAP
for turn_start,turn_end in TURN_SPANS:
    start=fraction(turn_start)
    end=1. if turn_end == LAP else fraction(turn_end)
    assert end-start > 2*runout, 'No full-bank turn core'
    banking_transitions.extend((
        dict(start=start,end=start+runout,fromDegrees=0.,toDegrees=BANK_DEGREES),
        dict(start=end-runout,end=end,fromDegrees=BANK_DEGREES,toDegrees=0.),
    ))

def bank(s):
    f=fraction(s)
    degrees=banking_transitions[-1]['toDegrees']
    for transition in banking_transitions:
        if f < transition['start']:
            break
        if f < transition['end']:
            t=(f-transition['start'])/(transition['end']-transition['start'])
            return transition['fromDegrees']+(transition['toDegrees']-transition['fromDegrees'])*quint(t)
        degrees=transition['toDegrees']
    return degrees

def surface(s,lane):
    # Keep the inside edge level so the racing ribbon meets the flat apron.
    b=math.radians(bank(s)); xy=point(s,-lane*math.cos(b)); return Vector((*xy,math.sin(b)*(lane+W/2)))

def pit_trace_bank_sample(s):
    # Preserve the existing authored horizontal pit routes when rebuilding the pavement.
    for start in (S,T3):
        if start<s<start+A:
            return BANK_DEGREES*smooth((s-start)/18)*smooth((start+A-s)/18)
    return 0.

pit_trace_bank_samples=[pit_trace_bank_sample(i*LAP/240) for i in range(240)]
def pit_trace_apron_center(s):
    f=(s%LAP)/LAP*240
    i=int(f)
    a,b=pit_trace_bank_samples[i],pit_trace_bank_samples[(i+1)%240]
    return W/2*math.cos(math.radians(a+(b-a)*smooth(f-i)))+APRON/2

def flat(p,h=.008): return Vector((*p,h))
def entry_path(s):
    offset=pit_trace_apron_center(s)
    return point(s,offset+(PIT_OFFSET-offset)*quint((s-ENTRY)/(ROAD_START-ENTRY)))

def exit_path(s):
    offset=pit_trace_apron_center(s)
    return point(s,PIT_OFFSET+(offset-PIT_OFFSET)*quint((s-ROAD_END)/(EXIT-ROAD_END)))
road_path=lambda s: point(s,PIT_OFFSET)
entry_s=stations(ENTRY,ROAD_START,1.5); road_s=stations(ROAD_START,ROAD_END,2.5); exit_s=stations(ROAD_END,EXIT,1.5)
entry=[entry_path(s) for s in entry_s]; road=[road_path(s) for s in road_s]; exit=[exit_path(s) for s in exit_s]
# The game extrapolates open-spline endpoint handles from the nearest control.
# Align these handles explicitly on both sides of every route boundary.
def tangent(fn,s): return (fn(s+.01)-fn(s-.01)).normalized()
for path,fn,ss in ((entry,entry_path,entry_s),(road,road_path,road_s),(exit,exit_path,exit_s)):
    path[1]=path[0]+tangent(fn,ss[0])*(path[1]-path[0]).length
    path[-2]=path[-1]-tangent(fn,ss[-1])*(path[-1]-path[-2]).length
def sampled_path(points,ss,s):
    i=max(0,min(len(ss)-2,bisect.bisect_right(ss,s)-1)); t=(s-ss[i])/(ss[i+1]-ss[i])
    b,c=points[i:i+2]; a=points[i-1] if i else 2*b-c; d=points[i+2] if i+2<len(points) else 2*c-b
    return catmull(a,b,c,d,t)
entry_path=lambda s: sampled_path(entry,entry_s,s)
exit_path=lambda s: sampled_path(exit,exit_s,s)
road_path=lambda s: sampled_path(road,road_s,s)
# Equal physical spacing on the inside stall baseline, not on the longer racing line.
stall_s=stations(ROAD_START+18,ROAD_END-18,.3)
stall_p=[point(s,PIT_OFFSET+4.2) for s in stall_s]
dist=[0.]
for a,b in zip(stall_p,stall_p[1:]): dist.append(dist[-1]+(b-a).length)
stall_stations=[]
for i in range(43):
    target=dist[-1]*i/42; k=min(len(dist)-2,max(0,bisect.bisect_right(dist,target)-1))
    t=(target-dist[k])/(dist[k+1]-dist[k]); stall_stations.append(stall_s[k]+(stall_s[k+1]-stall_s[k])*t)
stalls=[point(s,PIT_OFFSET+4.2) for s in stall_stations]
def xz(v): return dict(x=round(v.x,6),z=round(v.y,6))
definition=dict(id=33,name='Martinsville Speedway',location=dict(city='Martinsville',state='VA'),lengthMiles=.526,
    aspectRatio=(S+2*R)/(2*R),majorStyle='Oval',minorStyle='Oval',surface='Asphalt',restrictorPlate=False,
    banking=dict(turns12=12,turns34=12,frontStraight=0,backStraight=0),widthMeters=W,pitStalls=43,
    NumberOfQualifyingLaps=2,raceLaps=500,raceStageLaps=[130,130,240],qualifyingReferenceMph=.526*3600/RECORD,
    cornerSpeedCalibration=1.15,
    weights=dict(surfaceRoughness=.55,aerodynamics=.15,horsepower=.45,brakes=1,suspension=.9,transmission=.25,handling=.9,tireWear=.7),
    layoutPoints=[dict(xz(p),banking=bank(i*LAP/240),apronWidthMeters=APRON) for i,p in enumerate(controls)],
    bankingTransitions=banking_transitions,
    raceMarkers=dict(hasStartFinish=True,startFinish=fraction(FINISH),hasRestartZone=True,restartZoneStart=fraction(RESTART[0]),restartZoneEnd=fraction(RESTART[1])),
    pitRoad=dict(enabled=True,widthMeters=PIT_WIDTH,speedLimitMph=30,speedLimitStart=0,speedLimitEnd=1,
                 points=list(map(xz,road)),entrance=list(map(xz,entry)),exit=list(map(xz,exit)),stalls=list(map(xz,stalls))))
(OUT/'33.json').write_text(json.dumps(definition,indent=2)+'\n',encoding='utf-8')

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene; scene.unit_settings.system='IMPERIAL'
scene['track_id']=33; scene['qualifying_reference_seconds']=RECORD
scene['qualifying_reference_mph']=definition['qualifyingReferenceMph']
scene['qualifying_reference_source']='https://www.nascar.com/gallery/nascar-all-time-qualifying-records-by-track/'
scene['pit_route']='Turn 3 entry; around turns 3-4, frontstretch, turns 1-2; Turn 2 exit. Backstretch apron handoffs.'
scene['estimated_dimensions']='Pit width 39.4 ft, apron 26.2 ft, wall 3.5 ft, restart zone endpoints, stall spacing. See geometry-notes.md.'
scene['estimated_banking_profile']='Flat inside-edge datum. Four estimated 60 m quintic runouts, 0 degrees on straights and 12 degrees at turn cores.'
scene['banking_transitions']=json.dumps(banking_transitions)
def material(name,color):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF'); bs.inputs['Base Color'].default_value=(*color,1); bs.inputs['Roughness'].default_value=.9
    return m
asphalt=material('Racing asphalt',(.12,.13,.145)); concrete=material('Racing concrete',(.46,.45,.42))
apron_mat=material('Apron asphalt',(.19,.20,.21)); pit_mat=material('Pit concrete',(.38,.37,.35))
white=material('White marking',(.8,.8,.77)); wallmat=material('Concrete wall',(.7,.7,.67))
black=material('Black checks',(.025,.025,.025)); yellow=material('Yellow marking',(.8,.6,.03))
red=material('Restart red',(.6,.025,.015))
def mesh(name,verts,faces,mat):
    data=bpy.data.meshes.new(name); data.from_pydata(verts,[],faces); data.update()
    ob=bpy.data.objects.new(name,data); scene.collection.objects.link(ob); ob.data.materials.append(mat)
    # World-scaled UVs also support the game's pavement material textures.
    uv=data.uv_layers.new(name='Pavement UV')
    for loop in data.loops:
        v=data.vertices[loop.vertex_index].co; uv.data[loop.index].uv=(v.x/8,v.y/8)
    return ob
def ribbon(name,rows,mat,closed=False,smooth_faces=False):
    vs=[v for row in rows for v in row]; n=len(rows[0]); faces=[]
    for i in range(len(rows) if closed else len(rows)-1):
        next_row=(i+1)%len(rows)*n
        for j in range(n-1):
            a=i*n+j; b=next_row+j; face=(a,a+1,b+1,b)
            # Unity uses back-face culling. Blender Workbench's two-sided preview
            # hid inverted pavement; consistently face every ribbon upward.
            if (vs[face[1]]-vs[face[0]]).cross(vs[face[2]]-vs[face[0]]).z < 0:
                face=tuple(reversed(face))
            faces.append(face)
    ob=mesh(name,vs,faces,mat)
    ob.data.calc_loop_triangles()
    for face in ob.data.loop_triangles:
        assert face.area > 0. and face.normal.z > 0., f'{name}: invalid pavement at face {face.polygon_index}'
    if smooth_faces:
        for face in ob.data.polygons:
            face.use_smooth=True
    return ob
def wall(name,rows,closed=False):
    # Explicit vertical wall mesh: thickness extends outward, constant height above local pavement.
    verts=[]; faces=[]
    for a,b in rows: verts.extend((a,b,b+Vector((0,0,1.067)),a+Vector((0,0,1.067))))
    for i in range(len(rows) if closed else len(rows)-1):
        next_row=(i+1)%len(rows)*4
        for j in range(4): faces.append((4*i+j,4*i+(j+1)%4,next_row+(j+1)%4,next_row+j))
    if not closed:
        faces.extend(((3,2,1,0),tuple(range(len(verts)-4,len(verts)))))
    # Determine the face order from the bottom of the wall.
    a,b,c,_=faces[0]
    if (verts[b]-verts[a]).cross(verts[c]-verts[a]).z > 0:
        faces=[tuple(reversed(face)) for face in faces]
    return mesh(name,verts,faces,wallmat)

# The full-width ribbon shares vertices at the asphalt/concrete boundary. The
# apron and the wall base use exactly the same stations and racing edge points.
breaks=sorted((0.,S,S+A,T3,LAP,
               *(station(t['end']) for t in banking_transitions[::2]),
               *(station(t['start']) for t in banking_transitions[1::2])))
ds=[s for a,b in zip(breaks,breaks[1:]) for s in stations(a,b,.5)[:-1]]
racing_rows=[[surface(s,lane) for lane in (W/2,0.,-W/2)] for s in ds]
racing=ribbon('Racing surface',racing_rows,asphalt,closed=True,smooth_faces=True)
racing['lap_fractions']=[fraction(s) for s in ds]
racing.data.materials.append(concrete)
for row,s in enumerate(ds):
    if any(start <= s < end for start,end in TURN_SPANS):
        racing.data.polygons[row*2+1].material_index=1
apron_rows=[[row[-1],flat(point(s,W/2*math.cos(math.radians(bank(s)))+APRON),0)] for s,row in zip(ds,racing_rows)]
ribbon('Continuous inside apron',apron_rows,apron_mat,closed=True)
wall_rows=[[row[0],row[0]-flat(inward(s)*.35,0)] for s,row in zip(ds,racing_rows)]
wall('Outside wall follows banked racing edge',wall_rows,closed=True)

def path_rows(fn,ss,kind):
    result=[]
    for s in ss:
        t=(s-ss[0])/(ss[-1]-ss[0]); p=fn(s)
        tangent=(fn(min(ss[-1],s+.05))-fn(max(ss[0],s-.05))).normalized(); n=Vector((-tangent.y,tangent.x))
        # Preserve full road width before the driving lane begins to move laterally.
        width=PIT_WIDTH if kind=='road' else 3.6+(PIT_WIDTH-3.6)*smooth((t if kind=='entry' else 1-t)*5)
        clearance=(p-center(s)).dot(inward(s))-W/2*math.cos(math.radians(bank(s)))
        width=min(width,2*(clearance-.12))
        result.append([flat(p-n*width/2),flat(p+n*width/2)])
    return result
ribbon('Pit entry T3 continuous apron connector',path_rows(entry_path,entry_s,'entry'),pit_mat)
ribbon('Pit road turns and frontstretch',path_rows(road_path,road_s,'road'),pit_mat)
ribbon('Pit exit T2 continuous apron connector',path_rows(exit_path,exit_s,'exit'),pit_mat)
wall('Pit separator wall track side',[[flat(point(s,PIT_OFFSET-PIT_WIDTH/2-.35)),flat(point(s,PIT_OFFSET-PIT_WIDTH/2))] for s in stations(ROAD_START+8,ROAD_END-8,.7)])
wall('Pit wall behind stalls',[[flat(point(s,PIT_OFFSET+PIT_WIDTH/2)),flat(point(s,PIT_OFFSET+PIT_WIDTH/2+.35))] for s in stations(ROAD_START+8,ROAD_END-8,.7)])
for i,s in enumerate(stall_stations):
    v=point(s,PIT_OFFSET+4.2); n=inward(s); t=Vector((n.y,-n.x))
    ribbon('Pit stall boundary %02d'%(i+1),[[flat(v+t*along+n*side,.022) for side in (-1.65,1.65)] for along in (-3.15,-3.02)],yellow)
for lane in (-W/2+.1,W/2-.1):
    ribbon('White racing edge',[[surface(s,lane-.06)+Vector((0,0,.018)),surface(s,lane+.06)+Vector((0,0,.018))] for s in ds],white,closed=True,smooth_faces=True)
for row in range(2):
    for i in range(28):
        a=-W/2+i*W/28
        ribbon('Start finish checker %d %d'%(row,i),[[surface(s,l)+Vector((0,0,.02)) for l in (a,a+W/28)] for s in (FINISH+(row-1)*.6,FINISH+row*.6)],black if (row+i)%2 else white)
ribbon('Restart zone outside edge',[[surface(s,l)+Vector((0,0,.025)) for l in (W/2-.2,W/2-1.1)] for s in stations(*RESTART)],red)
for s in RESTART:
    ribbon('Restart zone endpoint',[[surface(t,l)+Vector((0,0,.03)) for l in (W/2-.2,W/2-2.5)] for t in (s-.2,s+.2)],yellow)
for s in (ROAD_START+3,ROAD_END-3):
    ribbon('Pit speed line',[[flat(point(t,o),.02) for o in (PIT_OFFSET-6,PIT_OFFSET+6)] for t in (s-.15,s+.15)],yellow)

def camera(name,position,scale):
    data=bpy.data.cameras.new(name); ob=bpy.data.objects.new(name,data); scene.collection.objects.link(ob)
    ob.location=position; ob.rotation_euler=(-ob.location).to_track_quat('-Z','Y').to_euler()
    data.type='ORTHO'; data.ortho_scale=scale; data.clip_end=3000; return ob
top=camera('Overhead',(0,0,700),425); top.rotation_euler=(0,0,0)
oblique=camera('Oblique',(145,-450,400),445)
low=camera('Turn 1 approach',surface(S-14,W/2-3.)+Vector((0,0,3.5)),1)
low.data.type='PERSP'; low.data.lens=42
low.rotation_euler=(surface(station(banking_transitions[0]['end']),0)-low.location).to_track_quat('-Z','Y').to_euler()
scene.render.engine='BLENDER_WORKBENCH'; scene.display.shading.light='STUDIO'; scene.display.shading.color_type='MATERIAL'
scene.display.shading.show_cavity=True; scene.display.shading.cavity_type='BOTH'
scene.display.shading.show_backface_culling=True
scene.world=bpy.data.worlds.new('Preview world')
scene.display.shading.background_type='WORLD'; scene.world.color=(.055,.065,.06)
scene.render.resolution_x=1800; scene.render.resolution_y=1000; scene.render.resolution_percentage=100
scene.camera=top
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'MartinsvilleSpeedway.blend'))
bpy.ops.object.select_all(action='DESELECT')
for ob in scene.objects:
    ob.select_set(ob.type=='MESH')
bpy.ops.export_scene.fbx(filepath=str(OUT/'MartinsvilleSpeedway.fbx'),use_selection=True,object_types={'MESH'},
    axis_forward='-Z',axis_up='Y',apply_unit_scale=True,add_leaf_bones=False,bake_anim=False,mesh_smooth_type='FACE')
# Reopen the deliverable before rendering, so previews verify the saved file.
bpy.ops.wm.open_mainfile(filepath=str(OUT/'MartinsvilleSpeedway.blend'))
scene=bpy.context.scene
for name,filename in (('Overhead','Martinsville_Overhead.png'),('Oblique','Martinsville_Oblique.png'),
                      ('Turn 1 approach','Martinsville_Turn1.png')):
    scene.camera=bpy.data.objects[name]; scene.render.filepath=str(OUT/filename); bpy.ops.render.render(write_still=True)
print('BUILD COMPLETE',json.dumps(dict(lap_m=lengths[-1],reference_seconds=RECORD,stall_pitch_m=dist[-1]/42,
                                       road_controls=len(road),banking_transitions=banking_transitions)))
