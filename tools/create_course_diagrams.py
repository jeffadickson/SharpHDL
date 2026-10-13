from pathlib import Path
from html import escape
import cairosvg

OUT=Path(__file__).resolve().parents[1]/'docs/course-art';OUT.mkdir(parents=True,exist_ok=True)
INK='#17324D';TEAL='#087E8B';ORANGE='#F29E4C';GREEN='#26734D';GREY='#E4E9EF';BLUE='#DFF2F4';CREAM='#FFF4DF'
class Art:
 def __init__(self,name,h=400):self.name=name;self.h=h;self.parts=[f'<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="{h}" viewBox="0 0 1000 {h}"><rect width="1000" height="{h}" fill="white"/>']
 def rect(self,x,y,w,h,fill=BLUE,stroke=INK,r=18):self.parts.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{fill}" stroke="{stroke}" stroke-width="3"/>')
 def circle(self,x,y,r,fill=ORANGE,stroke=INK):self.parts.append(f'<circle cx="{x}" cy="{y}" r="{r}" fill="{fill}" stroke="{stroke}" stroke-width="3"/>')
 def text(self,x,y,t,size=28,color=INK,anchor='middle',weight='normal'):self.parts.append(f'<text x="{x}" y="{y}" fill="{color}" text-anchor="{anchor}" font-family="DejaVu Sans, sans-serif" font-size="{size}" font-weight="{weight}">{escape(str(t))}</text>')
 def line(self,x1,y1,x2,y2,color=INK,width=4):self.parts.append(f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="{width}" stroke-linecap="round"/>')
 def arrow(self,x1,y,x2,color=TEAL):
  self.line(x1,y,x2-14,y,color,6);self.parts.append(f'<path d="M {x2-16} {y-10} L {x2} {y} L {x2-16} {y+10} Z" fill="{color}"/>')
 def robot(self,x,y,s=1):
  self.rect(x,y,100*s,80*s,BLUE,r=12);self.line(x+50*s,y,x+50*s,y-17*s);self.circle(x+50*s,y-24*s,7*s,ORANGE)
  self.circle(x+28*s,y+32*s,8*s,TEAL);self.circle(x+72*s,y+32*s,8*s,TEAL)
  self.line(x+32*s,y+58*s,x+68*s,y+58*s);self.rect(x+13*s,y+87*s,74*s,63*s,CREAM,r=10)
  self.line(x+14*s,y+115*s,x-10*s,y+101*s);self.line(x+87*s,y+115*s,x+110*s,y+95*s)
  self.line(x+32*s,y+150*s,x+27*s,y+174*s);self.line(x+67*s,y+150*s,x+74*s,y+174*s)
 def save(self):
  text=''.join(self.parts)+'</svg>';(OUT/(self.name+'.svg')).write_text(text)
  cairosvg.svg2png(bytestring=text.encode(),write_to=str(OUT/(self.name+'.png')),output_width=2000,output_height=2*self.h)

# A concept map, not a literal product photograph or placement guide.
a=Art('board',410);a.rect(200,30,600,350,'#C8E9DE');a.rect(424,175,152,126,INK);a.text(500,225,'FPGA',37,'white',weight='bold');a.text(500,262,'your circuit',21,'white')
a.rect(395,65,90,86,INK);a.rect(515,65,90,86,INK)
a.text(440,131,'B',58,'#FFDD8E');a.text(560,131,'A',58,'#FFDD8E')
for i in range(4):a.circle(305+i*130,338,18,GREEN if i!=1 else GREY)
for x,y in [(245,183),(245,261),(705,183),(705,261)]:a.rect(x,y,55,55,CREAM,r=8)
a.text(90,108,'Two digits',25);a.arrow(160,108,384)
a.text(887,195,'4 buttons',25);a.line(763,210,817,194,TEAL)
a.text(104,347,'4 LEDs',25);a.arrow(160,338,277)
a.save()

# Four weighted cards and a correct 1101 = thirteen example.
a=Art('binary_cards',370);a.text(500,40,'Make thirteen with the 8, 4, 2 and 1 cards',30,weight='bold')
for i,(weight,on) in enumerate(zip([8,4,2,1],[1,1,0,1])):
 x=80+i*225;a.rect(x,70,170,217,CREAM if on else GREY);a.text(x+85,142,weight,64,weight='bold');a.circle(x+85,194,23,GREEN if on else '#A7B2BE');a.text(x+85,256,f'{on} = '+('ON' if on else 'OFF'),24)
a.text(500,339,'8 + 4 + 0 + 1 = 13        Binary: 1101',30,weight='bold');a.save()

# Gate panels use rule boxes rather than ambiguous hand-drawn logic symbols.
a=Art('gates',380)
for x,title,rule,bits,result in [(30,'AND','BOTH must be ON',[1,0],0),(525,'OR','AT LEAST ONE is ON',[1,0],1)]:
 a.rect(x,35,440,295,CREAM);a.text(x+220,78,title,38,weight='bold');a.text(x+220,115,rule,21)
 for i,b in enumerate(bits):a.circle(x+90,175+i*80,27,GREEN if b else GREY);a.text(x+90,184+i*80,b,25,'white' if b else INK);a.line(x+120,175+i*80,x+220,215,TEAL)
 a.arrow(x+225,215,x+295);a.circle(x+350,215,35,GREEN if result else GREY);a.text(x+350,225,result,31,'white' if result else INK)
a.text(500,368,'1 means ON. 0 means OFF. The rule decides the answer.',25);a.save()

# Simple adder with values and explicit bit width.
a=Art('adder',340);a.robot(25,105,.9)
for x,label,detail in [(170,'B = 3','0011'),(415,'+ 2','0010'),(735,'A = 5','0101')]:
 a.rect(x,65,200,180,BLUE if x!=415 else CREAM);a.text(x+100,126,label,33,weight='bold');a.text(x+100,195,detail,37)
a.arrow(374,155,410);a.arrow(618,155,728)
a.text(500,306,'Four bits in. Four bits out. A changes when B changes.',26);a.save()

# Explicit overflow: the fifth bit is outside the chosen output bus.
a=Art('overflow',330);a.text(500,45,'Four places can hold only four bits',31,weight='bold')
a.text(130,115,'14 + 2 = 16',28)
for i,b in enumerate([1,0,0,0,0]):
 x=275+i*125;a.rect(x,90,100,140,CREAM if i==0 else BLUE);a.text(x+50,180,b,68,weight='bold');a.text(x+50,266,[16,8,4,2,1][i],25)
a.rect(393,80,493,162,'none',TEAL,r=13)
a.text(317,315,'Carry leaves',24);a.text(637,315,'Keep these four: 0000 = 0',25,weight='bold');a.save()

# Display segment labels A..G, and true pattern for digit two.
a=Art('segments',370)
segments={'a':(75,20,120,18),'b':(195,40,18,95),'c':(195,155,18,95),'d':(75,270,120,18),'e':(55,155,18,95),'f':(55,40,18,95),'g':(75,137,120,18)}
for base,digit in [(90,None),(440,2)]:
 for label,(x,y,w,h) in segments.items():
  on=digit is None or label in {'a','b','g','e','d'};a.rect(base+x,y+15,w,h,ORANGE if on else GREY,stroke='none',r=6)
 for label,(x,y) in {'a':(135,6),'b':(245,85),'c':(245,207),'d':(135,329),'e':(22,207),'f':(22,85),'g':(135,126)}.items():
  if digit is None:a.text(base+x,y+15,label.upper(),23)
a.text(830,104,'To show 2',31,weight='bold');a.text(830,158,'light A B D E G',25);a.text(830,214,'Five segments!',26);a.save()

# A clock is an event: a state box remembers an input.
a=Art('memory',350);a.rect(65,67,205,130,CREAM);a.text(167,119,'Input card',26);a.text(167,176,'8',46,weight='bold')
a.arrow(277,137,350);a.rect(355,50,300,190,BLUE);a.text(505,98,'REGISTER',31,weight='bold');a.text(505,160,'Still holding 3',29);a.text(505,208,'Wait for the clap',24)
a.arrow(665,137,740);a.rect(748,67,192,130,CREAM);a.text(844,120,'Output',26);a.text(844,176,'3',46,weight='bold');a.text(500,315,'Changing the input does not immediately change stored state.',25);a.save()

# Exact schematic clock edge indication.
a=Art('clock',250);a.text(120,82,'CLOCK',26,weight='bold');points=[(220,155),(345,155),(345,60),(470,60),(470,155),(595,155),(595,60),(720,60),(720,155),(845,155),(845,60),(945,60)]
for (x,y),(xx,yy) in zip(points,points[1:]):a.line(x,y,xx,yy,TEAL,6)
for x in [345,595,845]:a.line(x,30,x,187,ORANGE,3);a.text(x,222,'capture here',21)
a.save()

# Binary count sequence and explicitly ON/OFF coded values.
a=Art('counter',370);a.text(500,45,'After each slow counting tick while Run is ON',28,weight='bold')
for i,value in enumerate([0,1,2,3]):
 x=35+i*245;a.rect(x,80,215,222,CREAM);a.text(x+108,127,f'Count {value}',29,weight='bold')
 for j in range(4):
  bit=(value>>(3-j))&1;xx=x+35+j*48;a.circle(xx,193,18,GREEN if bit else GREY);a.text(xx,238,bit,25)
a.text(500,346,'The rightmost LED changes each tick: 0, 1, 0, 1 ...',27);a.save()

# Two real register stages, without suggesting local source variables create registers.
a=Art('pipeline',360)
for x,label,small in [(45,'INPUT','A: 3   B: 4'),(330,'+ 2','Stage 1 register'),(650,'x 3','Stage 2 register')]:
 a.rect(x,83,250,175,CREAM if x==45 else BLUE);a.text(x+125,137,label,39,weight='bold');a.text(x+125,193,small,23)
a.arrow(297,168,327);a.arrow(584,168,645);a.arrow(905,168,985)
a.text(500,42,'Two stages can work on different cards at the same time',27,weight='bold')
a.text(500,316,'A: 3 -> 5 -> 15          B: 4 -> 6 -> 18',29);a.save()

# Analogy for two-entry buffer, with clear indication output waiting is not dropped.
a=Art('skid',370);a.text(500,45,'A waiting tray saves the extra card when the receiver stops',27,weight='bold')
a.rect(45,125,180,150,CREAM);a.text(135,169,'Sender',29);a.text(135,218,'next card',24)
a.arrow(230,210,323)
a.rect(332,88,410,200,BLUE);a.rect(355,128,155,110,'white');a.rect(557,128,155,110,'white');a.text(432,175,'HEAD',25,weight='bold');a.text(432,216,'Card A',26);a.text(634,175,'SPILL',25,weight='bold');a.text(634,216,'Card B',26)
a.arrow(750,210,797);a.rect(805,115,160,163,CREAM);a.text(885,164,'Receiver',26);a.text(885,220,'WAIT',31,weight='bold');a.text(500,343,'Both slots full? Tell the sender to wait too. Never lose a card.',25);a.save()

# Cross-language blocks, language labels reflect source of actual implementation.
a=Art('languages',310)
for x,title,subtitle in [(55,'F#','Add two'),(365,'C#','Skid buffer'),(675,'Visual Basic','Multiply by three')]:
 a.rect(x,62,260,170,BLUE);a.text(x+130,116,title,31,weight='bold');a.text(x+130,178,subtitle,23)
a.arrow(322,151,356);a.arrow(632,151,666);a.text(500,285,'Different source languages. One connected hardware graph.',26);a.save()

# Capstone is illustrative, not a claim of a implemented decimal scoreboard.
a=Art('hockey',370);a.rect(665,65,240,150,BLUE);a.text(785,117,'SHOT COUNTER',24,weight='bold');a.text(785,173,'0  1  2  3 ...',29)
a.line(92,85,192,246,TEAL,12);a.line(192,246,275,258,TEAL,12);a.circle(340,263,30,INK);a.arrow(400,262,650)
a.robot(430,48,.8);a.text(500,335,'Press once for each shot. Predict the stored count first.',28);a.save()

# Cut-out number cards for physical binary activities.
a=Art('printable_cards',650)
for i,w in enumerate([8,4,2,1]):
 x=40+(i%2)*485;y=35+(i//2)*305;a.rect(x,y,435,255,CREAM,r=12);a.text(x+217,y+103,w,105,weight='bold');a.text(x+217,y+171,'ON adds this many',25);a.text(x+217,y+216,'OFF adds zero',25)
a.save()
print('Created',len(list(OUT.glob('*.svg'))),'exact SVG diagrams and PNG exports')
