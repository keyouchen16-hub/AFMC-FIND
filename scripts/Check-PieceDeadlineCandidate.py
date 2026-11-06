import pathlib
import types
import unittest

class Handle:
    def __init__(self):
        self.calls = []
        self.have = set()
    def reset_piece_deadline(self, index): self.calls.append(('reset', index))
    def prioritize_pieces(self, values): self.calls.append(('priorities', values))
    def have_piece(self, index): return index in self.have
    def set_piece_deadline(self, index, delay): self.calls.append(('deadline', index, delay))

class Worker: pass
namespace = {'Worker': Worker}
exec((pathlib.Path(__file__).with_name('PieceDeadlineCandidate.py')).read_text(), namespace)

class DeadlineChecks(unittest.TestCase):
    def make(self, offset=0, length=80*1024*1024, size=2801664):
        worker=Worker();worker.handle=Handle();worker.last_window=None
        worker.selected={'id':0,'length':length}
        worker.info=types.SimpleNamespace(piece_length=lambda:size,num_pieces=lambda:50,
            files=lambda:types.SimpleNamespace(file_offset=lambda _:offset))
        return worker
    def test_start_is_bounded_and_urgent(self):
        w=self.make();w.window(0,128*1024)
        self.assertEqual(w.last_window,(0,2))
        self.assertEqual(w.handle.calls[-3:],[('deadline',0,0),('deadline',1,2500),('deadline',2,5000)])
        self.assertEqual(w.handle.calls[0][1],[1,1,1]+[0]*47)
    def test_no_repeated_deadline_on_each_read(self):
        w=self.make();w.window(128*1024,128*1024);count=len(w.handle.calls)
        w.window(256*1024,128*1024);self.assertEqual(len(w.handle.calls),count)
    def test_seek_resets_outside_window(self):
        w=self.make();w.window(0,1);w.handle.calls=[]
        w.window(20*2801664,1)
        self.assertEqual([x for x in w.handle.calls if x[0]=='reset'],[('reset',0),('reset',1),('reset',2)])
        p=next(x[1] for x in w.handle.calls if x[0]=='priorities')
        self.assertEqual([i for i,v in enumerate(p) if v],list(range(20,26)))
    def test_file_boundary_and_offset(self):
        w=self.make(offset=3*2801664,length=1000);w.window(900,100)
        self.assertEqual(w.last_window,(3,3))
        self.assertEqual(w.handle.calls[-1],('deadline',3,0))
    def test_completed_piece_needs_no_deadline(self):
        w=self.make();w.handle.have={0,2};w.window(0,1)
        self.assertEqual([x for x in w.handle.calls if x[0]=='deadline'],[('deadline',1,2500)])

if __name__=='__main__': unittest.main()
