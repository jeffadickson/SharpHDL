(set-logic QF_BV)
(set-option :produce-models true)
(set-option :timeout 5000)
(declare-fun input_0 () (_ BitVec 32))
(assert (not (= ((_ zero_extend 24) ((_ extract 7 0) input_0))
        (bvxor (bvand input_0 #x000000ff) #x00000001))))

(check-sat)
(get-model)
