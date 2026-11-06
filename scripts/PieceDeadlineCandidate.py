# Experimental replacement, injected into an isolated test worker only.
# The preparer also disables sequential_download; do not mix both pickers.
def _deadline_window(self, start, count):
    size = self.info.piece_length()
    offset = self.info.files().file_offset(self.selected['id'])
    first = (offset + start) // size
    ahead = 8*1024*1024 if start == 0 else 16*1024*1024
    file_last = (offset + self.selected['length'] - 1) // size
    last = min(file_last, first + max(1, (ahead+size-1)//size)-1)
    if self.last_window == (first,last):
        return
    previous = self.last_window
    self.last_window = (first,last)
    if previous:
        for index in range(previous[0],previous[1]+1):
            if index < first or index > last:
                self.handle.reset_piece_deadline(index)
    priorities = [0]*self.info.num_pieces()
    for index in range(first,last+1):
        priorities[index] = 1
    self.handle.prioritize_pieces(priorities)
    for index in range(first,last+1):
        if not self.handle.have_piece(index):
            self.handle.set_piece_deadline(index,(index-first)*2500)

Worker.window = _deadline_window
